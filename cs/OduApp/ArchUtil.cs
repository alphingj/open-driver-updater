// OS arch + system-tool paths. Mirrors odu/arch.py (WOW64-safe).
using System;
using System.IO;

namespace OduApp
{
    public static class ArchUtil
    {
        public static string Canonical(string machine)
        {
            string m = (machine ?? "").ToLowerInvariant().Replace('_', '-');
            if (m == "amd64" || m == "x86-64" || m == "x64" || m == "em64t") return "x64";
            if (m == "arm64" || m == "aarch64" || m == "arm64e") return "arm64";
            return "x86";
        }

        public static string NativeArch()
        {
            string wow = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
            if (!string.IsNullOrEmpty(wow)) return Canonical(wow);
            string pa = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
            if (!string.IsNullOrEmpty(pa)) return Canonical(pa);
            return Environment.Is64BitOperatingSystem ? "x64" : "x86";
        }

        public static string System32()
        {
            string windir = Environment.GetEnvironmentVariable("SystemRoot");
            if (string.IsNullOrEmpty(windir)) windir = @"C:\Windows";
            string sys32 = Path.Combine(windir, "System32");
            if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
            {
                string native = Path.Combine(windir, "Sysnative");
                if (Directory.Exists(native)) return native;
            }
            return sys32;
        }

        public static string Tool(string name)
        {
            string p = Path.Combine(System32(), name);
            return File.Exists(p) ? p : name;
        }

        public static bool InfCompatible(string infText, string arch)
        {
            // Collect Manufacturer decorations: header suffixes + ",NTxxx" entries.
            var decos = new System.Collections.Generic.HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    infText, @"\[Manufacturer([^\]]*)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                AddDeco(decos, m.Groups[1].Value);
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    infText, @",\s*(NT[A-Z0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                decos.Add(m.Groups[1].Value.ToUpperInvariant());
            if (decos.Count == 0) return true;
            string a = Canonical(arch);
            if (a == "x64") return decos.Contains("NTAMD64") || decos.Contains("NT");
            if (a == "arm64") return decos.Contains("NTARM64") || decos.Contains("NT");
            return decos.Contains("NTX86") || decos.Contains("NT");
        }

        private static void AddDeco(System.Collections.Generic.HashSet<string> decos, string frag)
        {
            string f = (frag ?? "").Trim().ToUpperInvariant();
            if (f.StartsWith(".")) f = f.Substring(1);
            int cut = f.IndexOfAny(new char[] { ' ', '\t', ']' });
            if (cut >= 0) f = f.Substring(0, cut);
            if (f.Length > 0) decos.Add(f);
        }
    }
}
