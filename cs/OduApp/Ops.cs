// pnputil / schtasks / expand wrappers. All stdlib, admin needed for install.
using System;
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
                        done.WaitOne();
                        if (err != null) throw err;
                    }
                    else
                    {
                        wc.DownloadFile(url, dest);
                    }
                }
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

        public static OpResult RestorePoint()
        {
            return Run("powershell.exe",
                "-NoProfile -Command \"Try { Checkpoint-Computer " +
                "-Description 'Open Driver Updater' -RestorePointType 'MODIFY_SETTINGS' " +
                "-ErrorAction Stop; 'OK' } Catch { 'FAIL: ' + $_.Exception.Message }\"",
                5 * 60 * 1000);
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
            var r = new OpResult();
            string oem = (published ?? "").Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(oem, "^oem\\d+\\.inf$"))
            {
                r.Ok = false;
                r.Log = "refusing: not a recorded oem name";
                return r;
            }
            return Run(ArchUtil.Tool("pnputil.exe"), "/delete-driver " + oem + " /uninstall",
                       10 * 60 * 1000);
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
