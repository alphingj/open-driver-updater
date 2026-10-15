// JSON-file store in %LOCALAPPDATA%\OpenDriverUpdater. No NuGet needed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OduApp
{
    public static class Store
    {
        // Serializes all file access: parallel catalog lookups share cache.json.
        private static readonly object FileLock = new object();

        public static string Dir()
        {
            string d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenDriverUpdater");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }

        private static string Read(string name)
        {
            lock (FileLock)
            {
                try { return File.ReadAllText(Path.Combine(Dir(), name), Encoding.UTF8); }
                catch { return null; }
            }
        }

        private static void Write(string name, string content)
        {
            lock (FileLock)
            {
                try { File.WriteAllText(Path.Combine(Dir(), name), content, Encoding.UTF8); }
                catch { }
            }
        }

        private static List<string> ReadList(string name)
        {
            var out_ = new List<string>();
            string t = Read(name);
            if (t == null) return out_;
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(t, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                out_.Add(m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"));
            return out_;
        }

        public static List<string> Exclusions() { return ReadList("exclusions.json"); }

        public static void AddExclusion(string hwid)
        {
            if (string.IsNullOrEmpty(hwid)) return;
            List<string> l = Exclusions();
            if (!l.Contains(hwid)) l.Add(hwid);
            SaveExclusions(l);
        }

        public static void RemoveExclusion(string hwid)
        {
            List<string> l = Exclusions();
            l.Remove(hwid);
            SaveExclusions(l);
        }

        private static void SaveExclusions(List<string> l)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < l.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(l[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"");
            }
            Write("exclusions.json", sb.Append("]").ToString());
        }

        // Cache record: {"k":key,"t":epoch-sec,"v":escaped-value}.
        // Values are stored escaped exactly once (JStr); reads unescape once.
        // The record regex tolerates '{' '}' inside string values.
        private const string RecPat =
            "\\{\"k\":\"((?:[^\"\\\\]|\\\\.)*)\",\"t\":(\\d+),\"v\":\"((?:[^\"\\\\]|\\\\.)*)\"\\}";

        private static string Unesc(string s)
        {
            return s.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        public static string CacheGet(string key)
        {
            string t = Read("cache.json");
            if (t == null) return null;
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(t, RecPat))
            {
                if (Unesc(m.Groups[1].Value) != key) continue;
                long when;
                if (!long.TryParse(m.Groups[2].Value, out when)) return null;
                if (DateTime.UtcNow.Ticks / 10000000L - when >= 7 * 24 * 3600) return null;
                return Unesc(m.Groups[3].Value);
            }
            return null;
        }

        public static void CachePut(string key, string value)
        {
            var all = new Dictionary<string, KeyValuePair<string, long>>();
            string t = Read("cache.json");
            if (t != null)
            {
                foreach (System.Text.RegularExpressions.Match m in
                    System.Text.RegularExpressions.Regex.Matches(t, RecPat))
                {
                    long when;
                    if (long.TryParse(m.Groups[2].Value, out when))
                        all[Unesc(m.Groups[1].Value)] =
                            new KeyValuePair<string, long>(Unesc(m.Groups[3].Value), when);
                }
            }
            all[key] = new KeyValuePair<string, long>(value,
                DateTime.UtcNow.Ticks / 10000000L);
            var sb = new StringBuilder("[");
            bool first = true;
            int kept = 0;
            foreach (KeyValuePair<string, KeyValuePair<string, long>> kv in all)
            {
                if (kept++ >= 500) break;
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{\"k\":").Append(JStr(kv.Key)).Append(",\"t\":")
                  .Append(kv.Value.Value).Append(",\"v\":").Append(JStr(kv.Value.Key)).Append("}");
            }
            Write("cache.json", sb.Append("]").ToString());
        }

        private static string JStr(string s)
        {
            return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        public static void RecordBackup(string name, string path)
        {
            string line = DateTime.UtcNow.ToString("o") + "|" + name + "|" + path + "\r\n";
            lock (FileLock)
            {
                try
                {
                    string f = Path.Combine(Dir(), "backups.log");
                    File.AppendAllText(f, line, Encoding.UTF8);
                    // Cap the log: keep the newest 200 entries.
                    string[] lines = File.ReadAllLines(f, Encoding.UTF8);
                    if (lines.Length > 200)
                        File.WriteAllLines(f, new System.Collections.Generic.List<string>(
                            lines).GetRange(lines.Length - 200, 200), Encoding.UTF8);
                }
                catch { }
            }
        }

        public static List<string[]> Backups()
        {
            var out_ = new List<string[]>();
            string t = Read("backups.log");
            if (t == null) return out_;
            foreach (string line in t.Split(new char[] { '\r', '\n' },
                                            StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = line.Split('|');
                if (p.Length >= 3) out_.Add(p);
            }
            out_.Reverse();
            return out_;
        }

        // Packages THIS app installed (rollback allow-list). One oem per line:
        // oem<TAB>instance-id<TAB>name<TAB>epoch.
        public static void RecordInstall(string oem, string instanceId, string name)
        {
            oem = (oem ?? "").Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(oem, "^oem\\d+\\.inf$")) return;
            lock (FileLock)
            {
                try
                {
                    var keep = new List<string>();
                    string cur = Read("installs.log");
                    if (cur != null)
                    {
                        foreach (string line in cur.Split(
                            new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            if (!line.StartsWith(oem + "\t")) keep.Add(line);
                    }
                    keep.Add(oem + "\t" + (instanceId ?? "") + "\t" + (name ?? "") + "\t" +
                             (DateTime.UtcNow.Ticks / 10000000L));
                    if (keep.Count > 500) keep.RemoveRange(0, keep.Count - 500);
                    Write("installs.log", string.Join("\r\n", keep.ToArray()) + "\r\n");
                }
                catch { }
            }
        }

        public static bool IsRecorded(string oem)
        {
            oem = (oem ?? "").Trim().ToLowerInvariant();
            string t = Read("installs.log");
            if (t == null) return false;
            foreach (string line in t.Split(new char[] { '\r', '\n' },
                                            StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith(oem + "\t")) return true;
            return false;
        }

        public static void ForgetInstall(string oem)
        {
            oem = (oem ?? "").Trim().ToLowerInvariant();
            lock (FileLock)
            {
                try
                {
                    var keep = new List<string>();
                    string cur = Read("installs.log");
                    if (cur != null)
                    {
                        foreach (string line in cur.Split(
                            new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            if (!line.StartsWith(oem + "\t")) keep.Add(line);
                    }
                    Write("installs.log", string.Join("\r\n", keep.ToArray()) +
                          (keep.Count > 0 ? "\r\n" : ""));
                }
                catch { }
            }
        }
    }
}
