// MS Catalog resolver over HttpClient. Mirrors odu/catalog.py.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OduApp
{
    public class CatalogHit
    {
        public string Id;
        public string Title;
        public long Size;
        public string Url;
    }

    public static class Catalog
    {
        public const string SearchBase = "https://www.catalog.update.microsoft.com/Search.aspx?q=";
        public const string DialogUrl = "https://www.catalog.update.microsoft.com/DownloadDialog.aspx";
        private const string GuidPat = "[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}";

        private static readonly string[,] Vendors = new string[,]
        {
            { "VEN_8086", "Intel", "https://www.intel.com/content/www/us/en/download-center.html" },
            { "VEN_10DE", "NVIDIA", "https://www.nvidia.com/drivers" },
            { "VEN_1002", "AMD", "https://www.amd.com/en/support/downloads.html" },
            { "VEN_1022", "AMD", "https://www.amd.com/en/support/downloads.html" },
            { "VEN_10EC", "Realtek", "https://www.realtek.com/Download/Index" },
            { "VEN_14E4", "Broadcom", "https://www.broadcom.com/support/download-search" },
            { "VEN_0E8D", "MediaTek", "https://www.mediatek.com/products" },
            { "VEN_13D3", "AzureWave", "https://www.azurewave.com/support.aspx" },
            { "VEN_17AA", "Lenovo", "https://pcsupport.lenovo.com/drivers" },
            { "VEN_1043", "ASUS", "https://www.asus.com/support/download-center/" },
            { "VEN_1028", "Dell", "https://www.dell.com/support/home/drivers" },
            { "VEN_103C", "HP", "https://support.hp.com/drivers" },
        };

        private static readonly string[] SkipPrefixes = new string[]
        {
            "SWD\\", "ROOT\\", "STORAGE\\", "MMDEVAPI\\", "PRINTENUM\\",
            "BTH\\", "UEFI\\", "HTREE\\"
        };

        public static string VenOf(string hwid)
        {
            Match m = Regex.Match(hwid ?? "", @"(?:VEN|VID)_([0-9A-Fa-f]{4})");
            return m.Success ? "VEN_" + m.Groups[1].Value.ToUpperInvariant() : "";
        }

        public static void VendorHint(string hwid, out string vendor, out string url)
        {
            string v = VenOf(hwid);
            for (int i = 0; i < Vendors.GetLength(0); i++)
                if (Vendors[i, 0] == v) { vendor = Vendors[i, 1]; url = Vendors[i, 2]; return; }
            vendor = ""; url = "";
        }

        public static List<string> QueryCandidates(string hwid)
        {
            var out_ = new List<string>();
            hwid = (hwid ?? "").Trim();
            if (hwid == "") return out_;
            out_.Add(hwid);
            Match m = Regex.Match(hwid, @"((?:VEN|VID)_[0-9A-Fa-f]{4}&(?:DEV|PID)_[0-9A-Fa-f]{4})",
                                  RegexOptions.IgnoreCase);
            if (m.Success && !out_.Contains(m.Groups[1].Value)) out_.Add(m.Groups[1].Value);
            if (hwid.Contains("&"))
            {
                string head = "", rest = hwid;
                int bs = hwid.IndexOf('\\');
                if (bs >= 0) { head = hwid.Substring(0, bs); rest = hwid.Substring(bs + 1); }
                string[] parts = rest.Split('&');
                foreach (int n in new int[] { 2, 1 })
                {
                    if (parts.Length > n)
                    {
                        string s = (head != "" ? head + "\\" : "") + string.Join("&", parts, 0, n);
                        if (!out_.Contains(s)) out_.Add(s);
                    }
                }
            }
            return out_;
        }

        public static bool IsSkipped(string hwid)
        {
            string u = (hwid ?? "").ToUpperInvariant();
            foreach (string p in SkipPrefixes)
                if (u.StartsWith(p)) return true;
            return false;
        }

        private static HttpClient _http;
        private static HttpClient Http()
        {
            if (_http == null)
            {
                _http = new HttpClient();
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                _http.Timeout = TimeSpan.FromSeconds(30);
            }
            return _http;
        }

        // Runs with no SynchronizationContext affinity: blocking .Result here
        // is safe from any thread (callers must still stay off the UI thread
        // for responsiveness — Refresh/UpdateAll use Task workers).
        private static string FetchInner(string url, string postBody)
        {
            Task<string> t;
            if (postBody == null)
                t = Http().GetStringAsync(url);
            else
                t = Http().PostAsync(url, new StringContent(
                    postBody, Encoding.UTF8,
                    "application/x-www-form-urlencoded")).ContinueWith(r =>
                {
                    string s = r.Result.Content.ReadAsStringAsync().Result;
                    r.Result.Dispose();
                    return s;
                });
            string html = t.Result;
            return html.Length > 2 * 1024 * 1024 ? html.Substring(0, 2 * 1024 * 1024) : html;
        }

        private static string Fetch(string url, string postBody)
        {
            var args = new string[] { url, postBody };
            Task<string> outer = Task.Factory.StartNew(
                delegate { return FetchInner(args[0], args[1]); },
                TaskCreationOptions.LongRunning);
            return outer.Result;
        }

        public static List<CatalogHit> QueryOne(string query)
        {
            string cached = Store.CacheGet("q:" + query);
            if (cached != null) return DecodeHits(cached);
            List<CatalogHit> found = new List<CatalogHit>();
            try
            {
                string html = Fetch(SearchBase + Uri.EscapeDataString(query), null);
                found = ParseSearch(html);
            }
            catch { }
            // Never cache misses: a transient failure must not poison the
            // cache for 7 days.
            if (found.Count > 0)
            {
                try { Store.CachePut("q:" + query, EncodeHits(found)); } catch { }
            }
            return found;
        }

        public static string SearchUrlFor(string hwid)
        {
            // Static link most likely to hit: the VEN/DEV (or VID/PID) pair the
            // Catalog indexes. Full HWIDs with REV/SUBSYS suffixes score zero.
            List<string> cands = QueryCandidates(hwid);
            foreach (string q in cands)
                if (Regex.IsMatch(q, "^(?:VEN|VID)_[0-9A-Fa-f]{4}&(?:DEV|PID)_[0-9A-Fa-f]{4}$",
                                  RegexOptions.IgnoreCase))
                    return SearchBase + Uri.EscapeDataString(q);
            string best = cands.Count > 0 ? cands[cands.Count - 1] : hwid;
            return SearchBase + Uri.EscapeDataString(best ?? "");
        }

        public class QueryResult
        {
            public string Query;
            public List<CatalogHit> Hits;
        }

        public static List<CatalogHit> Lookup(string hwid)
        {
            string q;
            return LookupQ(hwid, out q);
        }

        public static List<CatalogHit> LookupQ(string hwid, out string query)
        {
            // Parallel across candidates; order preserved so the first
            // candidate with hits wins (most specific query first).
            query = "";
            List<string> qs = QueryCandidates(hwid);
            Task<QueryResult>[] tasks = new Task<QueryResult>[qs.Count];
            for (int i = 0; i < qs.Count; i++)
            {
                string q = qs[i];
                tasks[i] = Task.Factory.StartNew(delegate
                {
                    var r = new QueryResult();
                    r.Query = q;
                    r.Hits = QueryOne(q);
                    return r;
                }, TaskCreationOptions.LongRunning);
            }
            try { Task.WaitAll(tasks, 4 * 60 * 1000); } catch { }
            foreach (Task<QueryResult> t in tasks)
            {
                QueryResult r = null;
                try { r = t.IsCompleted ? t.Result : null; }
                catch { }
                if (r != null && r.Hits.Count > 0) { query = r.Query; return r.Hits; }
            }
            return new List<CatalogHit>();
        }

        public static bool SpecificQuery(string query)
        {
            if (Regex.IsMatch(query ?? "",
                    @"(?:VEN|VID)_[0-9A-Fa-f]{4}&(?:DEV|PID)_[0-9A-Fa-f]{4}"))
                return true;
            int n = 0;
            foreach (char c in (query ?? "")) if (c == '&') n++;
            return n >= 2;
        }

        private static readonly HashSet<string> Stopwords = new HashSet<string>(
            new string[] { "microsoft", "standard", "devices", "device", "system",
                "generic", "compatible", "software", "component", "control", "method" });

        private static HashSet<string> TitleTokens(string text)
        {
            var out_ = new HashSet<string>();
            foreach (Match m in Regex.Matches((text ?? "").ToLowerInvariant(), "[a-z0-9]+"))
                if (m.Value.Length >= 3 && !Stopwords.Contains(m.Value)) out_.Add(m.Value);
            return out_;
        }

        public static bool TitleMatchesDevice(string title, Device d)
        {
            HashSet<string> tt = TitleTokens(title);
            foreach (string src in new string[] { d.Manufacturer, d.DevClass, d.FriendlyName })
            {
                foreach (string t in TitleTokens(src))
                    if (tt.Contains(t)) return true;
            }
            return false;
        }

        public static List<CatalogHit> ParseSearch(string html)
        {
            var out_ = new List<CatalogHit>();
            MatchCollection guids = Regex.Matches(html, "goToDetails\\(\"(" + GuidPat + ")\"\\)",
                                                  RegexOptions.IgnoreCase);
            if (guids.Count == 0)
                guids = Regex.Matches(html, "goToDetails\\(&quot;(" + GuidPat + ")&quot;\\)",
                                      RegexOptions.IgnoreCase);
            var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(html, "(" + GuidPat + ")_link'[^>]*>\\s*([^<]{1,200})",
                                              RegexOptions.IgnoreCase))
                titles[m.Groups[1].Value] = WebUtility.HtmlDecode(
                    Regex.Replace(m.Groups[2].Value, "\\s+", " ").Trim());
            var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(html, "(" + GuidPat + ")_originalSize\"?>(\\d+)<",
                                              RegexOptions.IgnoreCase))
            {
                long v;
                if (long.TryParse(m.Groups[2].Value, out v)) sizes[m.Groups[1].Value] = v;
            }
            int n = 0;
            foreach (Match g in guids)
            {
                if (n++ >= 10) break;
                string title;
                if (!titles.TryGetValue(g.Groups[1].Value, out title) || title == "") continue;
                long sz;
                sizes.TryGetValue(g.Groups[1].Value, out sz);
                var hit = new CatalogHit();
                hit.Id = g.Groups[1].Value;
                hit.Title = title;
                hit.Size = sz;
                hit.Url = "https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=" + hit.Id;
                out_.Add(hit);
            }
            return out_;
        }

        private static string EncodeHits(List<CatalogHit> hits)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < hits.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"id\":\"").Append(hits[i].Id).Append("\",\"title\":")
                  .Append(JsonStr(hits[i].Title)).Append(",\"size\":").Append(hits[i].Size)
                  .Append(",\"url\":").Append(JsonStr(hits[i].Url)).Append("}");
            }
            return sb.Append("]").ToString();
        }

        private static string JsonStr(string s)
        {
            return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static List<CatalogHit> DecodeHits(string json)
        {
            var out_ = new List<CatalogHit>();
            foreach (Match m in Regex.Matches(json, "\\{\"id\":\"(" + GuidPat + ")\",\"title\":\"((?:[^\"\\\\]|\\\\.)*)\",\"size\":(\\d+),\"url\":\"((?:[^\"\\\\]|\\\\.)*)\"\\}"))
            {
                var h = new CatalogHit();
                h.Id = m.Groups[1].Value;
                h.Title = m.Groups[2].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                long.TryParse(m.Groups[3].Value, out h.Size);
                h.Url = m.Groups[4].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                out_.Add(h);
            }
            return out_;
        }

        public class CabFile
        {
            public string Url;
            public string Sha256B64;
        }

        public static List<CabFile> DownloadUrls(string updateId, long size)
        {
            var out_ = new List<CabFile>();
            if (!Regex.IsMatch(updateId ?? "", "^" + GuidPat + "$")) return out_;
            string payload = "[{\"size\":" + size + ",\"languages\":\"\",\"uidInfo\":\""
                + updateId + "\",\"updateID\":\"" + updateId + "\"}]";
            string body = "updateIDs=" + Uri.EscapeDataString(payload)
                + "&updateIDsBlockedForImport=&contentImport=&sku=&serverName=&ssl=&portNumber=&version=&protocol=";
            string html;
            try { html = Fetch(DialogUrl, body); }
            catch { return out_; }
            var urls = new List<KeyValuePair<string, string>>();
            foreach (Match m in Regex.Matches(html,
                "files\\[(\\d+)\\]\\.url\\s*=\\s*'(https://[A-Za-z0-9.\\-]*download\\.windowsupdate\\.com/[^']+)'",
                RegexOptions.IgnoreCase))
                urls.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
            var shas = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(html,
                "files\\[(\\d+)\\]\\.sha256\\s*=\\s*'([A-Za-z0-9+/=]+)'"))
                shas[m.Groups[1].Value] = m.Groups[2].Value;
            var seen = new HashSet<string>();
            foreach (KeyValuePair<string, string> kv in urls)
            {
                if (seen.Contains(kv.Value)) continue;
                seen.Add(kv.Value);
                var f = new CabFile();
                f.Url = kv.Value;
                string s;
                f.Sha256B64 = shas.TryGetValue(kv.Key, out s) ? s : "";
                out_.Add(f);
            }
            return out_;
        }

        // Surrogate-safe truncation: never split a UTF-16 pair.
        public static string Cut(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            int n = max;
            if (char.IsHighSurrogate(s[n - 1])) n--;
            return s.Substring(0, n);
        }

        // Best-guess driver version from a Catalog title (last dotted token).
        // Dates use slashes/colons so dotted tokens are versions in practice.
        public static int[] CatalogVersion(string title)
        {
            int[] best = new int[0];
            foreach (Match m in Regex.Matches(title ?? "", "\\d+(?:\\.\\d+){1,3}"))
            {
                string[] parts = m.Value.Split('.');
                var v = new int[parts.Length];
                bool ok = true;
                for (int i = 0; i < parts.Length; i++)
                    if (!int.TryParse(parts[i], out v[i])) { ok = false; break; }
                if (ok) best = v;
            }
            return best;
        }

        public static bool IsNewer(int[] available, string installed)
        {
            if (available == null || available.Length == 0) return false;
            MatchCollection nums = Regex.Matches(installed ?? "", "\\d+");
            if (nums.Count == 0) return true;
            for (int i = 0; i < 4; i++)
            {
                int a = i < available.Length ? available[i] : 0;
                int b = 0;
                if (i < nums.Count) int.TryParse(nums[i].Value, out b);
                if (a != b) return a > b;
            }
            return false;
        }

        private static string NormDate(string s)
        {
            Match m = Regex.Match(s ?? "", "(\\d{4})\\D?(\\d{2})\\D?(\\d{2})");
            return m.Success ? m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value : "";
        }

        // Second opinion for incomparable schemes: "yes" / "review" / "no".
        // "review" items are shown separately and never auto-installed.
        public static string FuzzyNewer(string title, string installedVersion,
                                       string installedDate)
        {
            int[] aver = CatalogVersion(title);
            if (aver.Length > 0 && installedVersion != "" && !IsNewer(aver, installedVersion))
            {
                string td = NormDate(title);
                string id = NormDate(installedDate);
                if (td != "" && id != "" && string.Compare(td, id) > 0) return "review";
                return "no";
            }
            if (aver.Length > 0) return IsNewer(aver, installedVersion) ? "yes" : "no";
            return installedVersion != "" ? "review" : "yes";
        }

        public static List<DriverUpdate> Resolve(List<Device> devices, List<string> excluded, bool live)
        {
            string arch = ArchUtil.NativeArch();
            var excl = new HashSet<string>(excluded ?? new List<string>());
            var cands = new List<Device>();
            foreach (Device d in devices)
            {
                string h = d.PrimaryHwId();
                if (h == "" || IsSkipped(h)) continue;
                if (excl.Contains(h) || excl.Contains(d.InstanceId)) continue;
                if (!live)
                {
                    bool needs = d.ProblemCode != 0 || d.Status == "ERROR" ||
                                 string.IsNullOrEmpty(d.DriverVersion);
                    if (d.Status == "DISCONNECTED") needs = false;
                    if (!needs) continue;
                }
                cands.Add(d);
            }
            // Parallel lookups; order preserved so results stay stable.
            // Each slot keeps the winning query for the relevance check.
            List<CatalogHit>[] hits = new List<CatalogHit>[cands.Count];
            string[] queries = new string[cands.Count];
            if (live && cands.Count > 0)
            {
                Task[] tasks = new Task[cands.Count];
                for (int i = 0; i < cands.Count; i++)
                {
                    int idx = i;
                    string hw = cands[idx].PrimaryHwId();
                    tasks[idx] = Task.Factory.StartNew(delegate
                    {
                        List<CatalogHit> r = new List<CatalogHit>();
                        string q = "";
                        try { r = LookupQ(hw, out q); }
                        catch { }
                        hits[idx] = r;
                        queries[idx] = q;
                    }, TaskCreationOptions.LongRunning);
                }
                try { Task.WaitAll(tasks, 30 * 60 * 1000); } catch { }
                for (int i = 0; i < hits.Length; i++)
                    if (hits[i] == null) hits[i] = new List<CatalogHit>();
            }
            var out_ = new List<DriverUpdate>();
            for (int i = 0; i < cands.Count; i++)
            {
                Device d = cands[i];
                string hwid = d.PrimaryHwId();
                string vendor, vurl;
                VendorHint(hwid, out vendor, out vurl);
                var u = new DriverUpdate();
                u.HwId = hwid;
                u.DeviceName = d.DisplayName();
                u.InstanceId = d.InstanceId;
                u.InstalledVersion = d.DriverVersion ?? "";
                u.Source = "ms-catalog";
                u.Arch = arch;
                u.Vendor = vendor;
                u.VendorUrl = vurl;
                if (live)
                {
                    List<CatalogHit> found = (hits != null && i < hits.Length && hits[i] != null)
                        ? hits[i] : new List<CatalogHit>();
                    if (found.Count > 0)
                    {
                        string wq = (queries != null && i < queries.Length) ? queries[i] : "";
                        if (!SpecificQuery(wq) && !TitleMatchesDevice(found[0].Title, d))
                            continue; // generic-query hit for unrelated hardware
                        int[] aver = CatalogVersion(found[0].Title);
                        string verdict = FuzzyNewer(found[0].Title, d.DriverVersion, d.DriverDate);
                        if (verdict == "no")
                            continue; // installed is current
                        u.AvailableVersion = Cut(found[0].Title, 64);
                        u.DownloadUrl = found[0].Url;
                        u.Title = found[0].Title;
                        u.UpdateId = found[0].Id;
                        u.Size = found[0].Size;
                        u.Confidence = verdict;
                        u.IsGpu = string.Compare(d.DevClass ?? "", "display", true) == 0;
                        out_.Add(u);
                        continue;
                    }
                }
                if (live)
                {
                    bool needs = d.ProblemCode != 0 || d.Status == "ERROR" ||
                                 string.IsNullOrEmpty(d.DriverVersion);
                    if (!needs) continue; // healthy + no catalog hit: nothing to show
                }
                u.AvailableVersion = string.IsNullOrEmpty(d.DriverVersion)
                    ? "Check Catalog" : "Update available";
                u.DownloadUrl = SearchUrlFor(hwid);
                u.Title = u.DeviceName + " — search Microsoft Update Catalog"
                    + (vendor != "" ? " (" + vendor + ")" : "");
                u.UpdateId = "";
                u.Confidence = "high";
                u.IsGpu = string.Compare(d.DevClass ?? "", "display", true) == 0;
                out_.Add(u);
            }
            return out_;
        }
    }
}
