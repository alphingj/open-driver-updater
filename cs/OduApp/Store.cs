// JSON-file store in %LOCALAPPDATA%\OpenDriverUpdater. No NuGet needed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OduApp
{
    public static class Store
    {
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
            try { return File.ReadAllText(Path.Combine(Dir(), name), Encoding.UTF8); }
            catch { return null; }
        }

        private static void Write(string name, string content)
        {
            try { File.WriteAllText(Path.Combine(Dir(), name), content, Encoding.UTF8); }
            catch { }
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

        public static string CacheGet(string key)
        {
            string t = Read("cache.json");
            if (t == null) return null;
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(t, "\\{[^{}]*\\}"))
            {
                string block = m.Value;
                if (block.Contains("\"k\":" + JStr(key)))
                {
                    System.Text.RegularExpressions.Match ts =
                        System.Text.RegularExpressions.Regex.Match(block, "\"t\":(\\d+)");
                    long when;
                    if (ts.Success && long.TryParse(ts.Groups[1].Value, out when))
                    {
                        long now = DateTime.UtcNow.Ticks / 10000000L;
                        if (now - when < 7 * 24 * 3600)
                        {
                            System.Text.RegularExpressions.Match v =
                                System.Text.RegularExpressions.Regex.Match(
                                    block, "\"v\":\"((?:[^\"\\\\]|\\\\.)*)\"");
                            if (v.Success)
                                return v.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                        }
                    }
                    return null;
                }
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
                    System.Text.RegularExpressions.Regex.Matches(t, "\\{[^{}]*\\}"))
                {
                    string block = m.Value;
                    System.Text.RegularExpressions.Match k =
                        System.Text.RegularExpressions.Regex.Match(
                            block, "\"k\":\"((?:[^\"\\\\]|\\\\.)*)\"");
                    System.Text.RegularExpressions.Match ts =
                        System.Text.RegularExpressions.Regex.Match(block, "\"t\":(\\d+)");
                    System.Text.RegularExpressions.Match v =
                        System.Text.RegularExpressions.Regex.Match(
                            block, "\"v\":\"((?:[^\"\\\\]|\\\\.)*)\"");
                    long when;
                    if (k.Success && v.Success && ts.Success &&
                        long.TryParse(ts.Groups[1].Value, out when))
                        all[k.Groups[1].Value] = new KeyValuePair<string, long>(v.Groups[1].Value, when);
                }
            }
            all[key] = new KeyValuePair<string, long>(Esc(value),
                DateTime.UtcNow.Ticks / 10000000L);
            var sb = new StringBuilder("[");
            bool first = true;
            int kept = 0;
            foreach (KeyValuePair<string, KeyValuePair<string, long>> kv in all)
            {
                if (kept++ > 500) break;
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

        private static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public static void RecordBackup(string name, string path)
        {
            string line = DateTime.UtcNow.ToString("o") + "|" + name + "|" + path + "\r\n";
            try { File.AppendAllText(Path.Combine(Dir(), "backups.log"), line, Encoding.UTF8); }
            catch { }
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
    }
}
