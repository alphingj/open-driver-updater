// pnputil / schtasks / expand wrappers. All stdlib, admin needed for install.
using System;
using System.Diagnostics;
using System.IO;

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
            string dest = Path.Combine(destRoot,
                "full-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
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
                    string text;
                    try { text = File.ReadAllText(inf); }
                    catch { continue; }
                    if (ArchUtil.InfCompatible(text, arch)) { pick = inf; break; }
                }
                if (pick == "") { r.Ok = false; r.Log = "no " + arch + " INF in CAB"; return r; }
                r = InstallInf(pick);
                r.Path = pick;
                return r;
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static bool Download(string url, string dest, string shaB64)
        {
            try
            {
                using (var wc = new System.Net.WebClient())
                {
                    wc.Headers.Add("User-Agent", "Mozilla/5.0");
                    wc.DownloadFile(url, dest);
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

        public static string ScanCommand()
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            return "\"" + exe + "\" --selftest-scan";
        }
    }
}
