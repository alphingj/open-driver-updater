// pnputil / schtasks / expand wrappers. All stdlib, admin needed for install.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OduApp
{
    public class OpResult
    {
        public bool Ok;
        public string Log;
        public string Path;
    }

    public static class Ops
    {
        private static OpResult Run(string exe, string args, int timeoutMs)
        {
            var r = new OpResult();
            r.Ok = false; r.Log = ""; r.Path = "";
            try
            {
                var psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    // Async-drain both pipes first: pnputil output exceeds pipe
                    // buffers and would deadlock WaitForExit otherwise.
                    var stdout = new System.Text.StringBuilder();
                    var stderr = new System.Text.StringBuilder();
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) stdout.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) stderr.AppendLine(e.Data);
                    };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } r.Log = "timed out"; return r; }
                    p.WaitForExit();
                    string all = stdout.ToString() + stderr.ToString();
                    r.Ok = p.ExitCode == 0;
                    r.Log = all.Length > 2000 ? all.Substring(all.Length - 2000) : all;
                    return r;
                }
            }
            catch (Exception e) { r.Log = e.Message; return r; }
        }

        public static OpResult BackupAll(string destRoot)
        {
            var r = new OpResult();
            // Millisecond stamp: two backups in the same second must not share
            // a directory (pnputil refuses non-empty targets).
            string dest = Path.Combine(destRoot,
                "full-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            try { Directory.CreateDirectory(dest); }
            catch (Exception e) { r.Ok = false; r.Log = e.Message; return r; }
            r = Run(ArchUtil.Tool("pnputil.exe"), "/export-driver * \"" + dest + "\"", 30 * 60 * 1000);
            r.Path = dest;
            if (r.Ok) Store.RecordBackup("full-backup", dest);
            return r;
        }

        public static OpResult InstallInf(string inf)
        {
            return Run(ArchUtil.Tool("pnputil.exe"), "/add-driver \"" + inf + "\" /install",
                       10 * 60 * 1000);
        }

        // INF text with BOM sniffing: UTF-16 INFs are the common case and
        // must not be misread as UTF-8 (mojibake hides [Manufacturer]).
        public static string ReadInfText(string path)
        {
            try
            {
                byte[] raw = File.ReadAllBytes(path);
                if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
                    return Encoding.Unicode.GetString(raw);
                if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
                    return Encoding.BigEndianUnicode.GetString(raw);
                if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
                    return Encoding.UTF8.GetString(raw, 3, raw.Length - 3);
                try { return Encoding.UTF8.GetString(raw); }
                catch { return Encoding.Default.GetString(raw); }
            }
            catch { return ""; }
        }

        public static OpResult InstallCab(string cab, string arch)
        {
            var r = new OpResult();
            string dir = Path.Combine(Path.GetTempPath(), "odu-" + Guid.NewGuid().ToString("N"));
            try
            {
                string exp = Path.Combine(dir, "expanded");
                Directory.CreateDirectory(exp);
                OpResult x = Run(ArchUtil.Tool("expand.exe"),
                    "\"" + cab + "\" -F:* \"" + exp + "\"", 5 * 60 * 1000);
                if (!x.Ok) { r.Ok = false; r.Log = "expand failed: " + x.Log; return r; }
                string[] infs = Directory.GetFiles(exp, "*.inf", SearchOption.AllDirectories);
                if (infs.Length == 0) { r.Ok = false; r.Log = "no INF in CAB"; return r; }
                string pick = "";
                foreach (string inf in infs)
                {
                    if (ArchUtil.InfCompatible(ReadInfText(inf), arch)) { pick = inf; break; }
                }
                if (pick == "") { r.Ok = false; r.Log = "no " + arch + " INF in CAB"; return r; }
                r = InstallInf(pick);
                r.Path = pick;
                return r;
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static bool Download(string url, string dest, string shaB64,
                                      Action<int> percent)
        {
            try
            {
                Exception last = null;
                for (int attempt = 0; attempt < 2; attempt++) // one retry
                {
                    try
                    {
                        DownloadOnce(url, dest, percent);
                        last = null;
                        break;
                    }
                    catch (Exception e) { last = e; }
                }
                if (last != null) throw last;
                if (!string.IsNullOrEmpty(shaB64))
                {
                    byte[] expect;
                    try { expect = Convert.FromBase64String(shaB64); }
                    catch { expect = null; }
                    if (expect != null)
                    {
                        using (var sha = System.Security.Cryptography.SHA256.Create())
                        using (var fs = File.OpenRead(dest))
                        {
                            byte[] got = sha.ComputeHash(fs);
                            if (got.Length != expect.Length) { File.Delete(dest); return false; }
                            for (int i = 0; i < got.Length; i++)
                                if (got[i] != expect[i]) { File.Delete(dest); return false; }
                        }
                    }
                }
                return true;
            }
            catch { try { File.Delete(dest); } catch { } return false; }
        }

        private static void DownloadOnce(string url, string dest, Action<int> percent)
        {
            using (var wc = new System.Net.WebClient())
            {
                wc.Headers.Add("User-Agent", "Mozilla/5.0");
                    if (percent != null)
                    {
                        var done = new System.Threading.ManualResetEvent(false);
                        Exception err = null;
                        wc.DownloadProgressChanged += delegate(object s,
                            System.Net.DownloadProgressChangedEventArgs e)
                        {
                            try { percent(e.ProgressPercentage); } catch { }
                        };
                        wc.DownloadFileCompleted += delegate(object s,
                            System.ComponentModel.AsyncCompletedEventArgs e)
                        {
                            if (e.Error != null) err = e.Error;
                            done.Set();
                        };
                        wc.DownloadFileAsync(new Uri(url), dest);
                        // Async WebClient ignores Timeout: bound the wait ourselves.
                        if (!done.WaitOne(30 * 60 * 1000))
                        {
                            try { wc.CancelAsync(); } catch { }
                            throw new TimeoutException("download stalled");
                        }
                        if (err != null) throw err;
                    }
                    else
                    {
                        wc.DownloadFile(url, dest);
                    }
            }
        }

        public static OpResult Sched(string args)
        {
            return Run("schtasks.exe", args, 60000);
        }

        public static OpResult SchedEnable()
        {
            return Sched("/Create /TN OpenDriverUpdaterScan /TR \"" + ScanCommand() +
                         "\" /SC WEEKLY /D SUN /ST 10:00 /F");
        }

        public static OpResult SchedDisable()
        {
            return Sched("/Delete /TN OpenDriverUpdaterScan /F");
        }

        public static OpResult SchedStatus()
        {
            return Sched("/Query /TN OpenDriverUpdaterScan");
        }

        public class DriverPackage
        {
            public string Oem;
            public string Original;
            public string Provider;
            public string Class;
            public string Version;
        }

        public static List<DriverPackage> EnumDrivers()
        {
            var out_ = new List<DriverPackage>();
            OpResult r = Run(ArchUtil.Tool("pnputil.exe"), "/enum-drivers", 120000);
            if (!r.Ok) return out_;
            var cur = new DriverPackage();
            foreach (string raw in r.Log.Split(new char[] { '\r', '\n' }))
            {
                string line = raw.Trim();
                if (line == "")
                {
                    if (cur.Oem != "") out_.Add(cur);
                    cur = new DriverPackage();
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string k = line.Substring(0, colon).Trim().ToLowerInvariant();
                string v = line.Substring(colon + 1).Trim();
                if (k == "published name" && System.Text.RegularExpressions.Regex.IsMatch(v, "^oem\\d+\\.inf$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    cur.Oem = v.ToLowerInvariant();
                else if (k == "original name") cur.Original = v;
                else if (k == "provider name") cur.Provider = v;
                else if (k == "class name") cur.Class = v;
                else if (k == "driver version") cur.Version = v;
            }
            if (cur.Oem != "") out_.Add(cur);
            return out_;
        }

        public static OpResult ExportDriver(string oem, string destRoot)
        {
            var r = new OpResult();
            oem = (oem ?? "").Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(oem, "^oem\\d+\\.inf$"))
            {
                r.Ok = false;
                r.Log = "refusing: not an oem name";
                return r;
            }
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string dest = Path.Combine(destRoot, oem + "-" + stamp);
            for (int n = 2; Directory.Exists(dest); n++)
                dest = Path.Combine(destRoot, oem + "-" + stamp + "-" + n);
            try { Directory.CreateDirectory(dest); }
            catch (Exception e) { r.Ok = false; r.Log = e.Message; return r; }
            r = Run(ArchUtil.Tool("pnputil.exe"), "/export-driver " + oem + " \"" + dest + "\"", 5 * 60 * 1000);
            r.Path = dest;
            if (r.Ok) Store.RecordBackup(oem, dest);
            return r;
        }

        public static OpResult CleanupDownloads(string dlDir, int maxAgeDays, long maxBytes)
        {
            // Failed CABs are kept for retry/debug, so bound the folder instead
            // of letting it grow forever. Never throws.
            var r = new OpResult();
            r.Ok = true;
            r.Log = "";
            r.Path = dlDir;
            try
            {
                Directory.CreateDirectory(dlDir);
                DateTime cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
                var keep = new List<FileInfo>();
                foreach (string f in Directory.GetFiles(dlDir, "*.cab"))
                {
                    var fi = new FileInfo(f);
                    if (fi.LastWriteTimeUtc < cutoff)
                    {
                        try { fi.Delete(); } catch { }
                    }
                    else keep.Add(fi);
                }
                long total = 0;
                foreach (FileInfo fi in keep) total += fi.Length;
                keep.Sort(delegate(FileInfo a, FileInfo b)
                {
                    return a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);
                });
                foreach (FileInfo fi in keep)
                {
                    if (total <= maxBytes) break;
                    try
                    {
                        long n = fi.Length;
                        fi.Delete();
                        total -= n;
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                r.Ok = false;
                r.Log = e.Message;
            }
            return r;
        }

        public static OpResult RestorePoint()
        {
            // powershell.exe exits 0 even when Checkpoint writes FAIL, so the
            // old code reported success unconditionally. Parse stdout instead.
            OpResult recent = RecentRestorePoint();
            if (recent.Ok) return recent;
            OpResult r = Run("powershell.exe",
                "-NoProfile -Command \"Try { Checkpoint-Computer " +
                "-Description 'Open Driver Updater' -RestorePointType 'MODIFY_SETTINGS' " +
                "-ErrorAction Stop; 'OK' } Catch { 'FAIL: ' + $_.Exception.Message }\"",
                5 * 60 * 1000);
            string stdout = (r.Log ?? "");
            var m = new OpResult();
            if (stdout.Trim() == "OK")
            {
                m.Ok = true;
                m.Log = "restore point created";
                return m;
            }
            if (stdout.Contains("1440") || stdout.Contains("already been created"))
            {
                m.Ok = true;
                m.Log = "recent restore point already exists (Windows allows " +
                        "one per 24 hours) — reused";
                return m;
            }
            if (RestorePointUnavailable(stdout))
            {
                m.Ok = true;
                m.Log = "System Protection is disabled on this PC — restore " +
                        "point skipped; the per-driver backup still runs";
                return m;
            }
            m.Ok = false;
            m.Log = stdout.Length > 500 ? stdout.Substring(stdout.Length - 500) : stdout;
            return m;
        }

        // System Protection off is common; it must not block every update.
        public static bool RestorePointUnavailable(string stdout)
        {
            string s = (stdout ?? "").ToLowerInvariant();
            return s.Contains("system restore is disabled")
                || s.Contains("not enabled on the system drive")
                || s.Contains("system protection is disabled")
                || s.Contains("0x80070422");
        }

        // True (ok) when a restore point from the last 24h exists.
        public static OpResult RecentRestorePoint()
        {
            var m = new OpResult();
            m.Ok = false;
            m.Log = "";
            try
            {
                OpResult r = Run("powershell.exe",
                    "-NoProfile -Command \"Get-ComputerRestorePoint | " +
                    "Sort-Object CreationTime -Descending | Select-Object -First 1 | " +
                    "ForEach-Object { $_.CreationTime.ToString('o') }\"",
                    120000);
                DateTime when;
                if (r.Ok && DateTime.TryParse(r.Log.Trim(), out when) &&
                    (DateTime.Now - when).TotalHours < 24)
                {
                    m.Ok = true;
                    m.Log = "recent restore point exists, skipped";
                }
            }
            catch { }
            return m;
        }

        public static bool RebootPending(out string reasons)
        {
            reasons = "";
            try
            {
                string[] checks = new string[]
                {
                    @"SYSTEM\CurrentControlSet\Control\Session Manager|PendingFileRenameOperations",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing|RebootPending",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update|RebootRequired"
                };
                foreach (string c in checks)
                {
                    int bar = c.IndexOf('|');
                    string key = c.Substring(0, bar);
                    string val = c.Substring(bar + 1);
                    using (Microsoft.Win32.RegistryKey k =
                        Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key))
                    {
                        if (k != null && k.GetValue(val) != null)
                        {
                            reasons += (reasons == "" ? "" : ", ") + val;
                        }
                    }
                }
            }
            catch { }
            return reasons != "";
        }

        public static OpResult RollbackDriver(string published)
        {
            return RollbackDriver(published, "", "");
        }

        // Remove the package we installed, then re-install the exported package
        // that was bound to the device before the upgrade (true rollback).
        public static OpResult RollbackDriver(string published, string prevOem, string prevPath)
        {
            var r = new OpResult();
            string oem = (published ?? "").Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(oem, "^oem\\d+\\.inf$"))
            {
                r.Ok = false;
                r.Log = "refusing: not a recorded oem name";
                return r;
            }
            OpResult del = Run(ArchUtil.Tool("pnputil.exe"),
                               "/delete-driver " + oem + " /uninstall", 10 * 60 * 1000);
            if (!del.Ok) return del;
            if (string.IsNullOrEmpty(prevOem) || string.IsNullOrEmpty(prevPath))
                return del;
            string inf = ExportedInf(prevPath);
            if (inf == "")
            {
                del.Log = (del.Log ?? "") + "\nprevious driver was not found at " + prevPath;
                return del;
            }
            OpResult add = Run(ArchUtil.Tool("pnputil.exe"),
                               "/add-driver \"" + inf + "\" /install", 10 * 60 * 1000);
            var m = new OpResult();
            m.Ok = add.Ok;
            m.Path = inf;
            m.Log = (add.Ok ? "restored " + prevOem.ToLowerInvariant() + "\n" : "") +
                    (del.Log ?? "") + "\n--- restore " + prevOem + " ---\n" + (add.Log ?? "");
            return m;
        }

        // First .inf inside a pnputil /export-driver folder ("" if none).
        public static string ExportedInf(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    string[] files = Directory.GetFiles(folder, "*.inf", SearchOption.AllDirectories);
                    if (files.Length > 0) return files[0];
                }
            }
            catch { }
            return "";
        }

        // Published oemNN.inf currently bound to a device ("" if none/in-box).
        public static string CurrentInf(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return "";
            OpResult r = Run(ArchUtil.Tool("pnputil.exe"),
                             "/enum-devices /instanceid \"" + instanceId + "\" /drivers",
                             60000);
            if (!r.Ok) return "";
            return ParseCurrentInf(r.Log);
        }

        // The active "Driver Name" is printed before extension/matching drivers,
        // so the first oem token is the package bound to the device.
        public static string ParseCurrentInf(string text)
        {
            System.Text.RegularExpressions.Match m =
                System.Text.RegularExpressions.Regex.Match(text ?? "",
                    "\\b(oem\\d+\\.inf)\\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
        }

        public static string PublishedName(string log)
        {
            System.Text.RegularExpressions.Match m =
                System.Text.RegularExpressions.Regex.Match(log ?? "",
                    "Published name\\s*:\\s*(oem\\d+\\.inf)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
        }

        public static string ScanCommand()
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            return "\"" + exe + "\" --selftest-scan";
        }
    }
}
