// Runs native odu_scan.exe and parses its JSON. Falls back to "" on failure.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

namespace OduApp
{
    public static class NativeScan
    {
        public static string ExeDir()
        {
            return Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        public static string FindScanner()
        {
            string root = ExeDir();
            for (int up = 0; up < 6 && root != null; up++)
            {
                string[] cands = new string[]
                {
                    Path.Combine(root, "native", "bin", "x64", "odu_scan.exe"),
                    Path.Combine(root, "native", "bin", "x86", "odu_scan.exe"),
                };
                foreach (string c in cands)
                    if (File.Exists(c)) return c;
                root = Path.GetDirectoryName(root);
            }
            return "";
        }

        public static List<Device> Scan(int timeoutMs)
        {
            string exe = FindScanner();
            if (exe == "") return new List<Device>();
            try
            {
                var psi = new ProcessStartInfo(exe);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    // Read stdout BEFORE WaitForExit: the 100KB+ JSON would
                    // deadlock a 4KB pipe if we waited first.
                    string json = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return new List<Device>(); }
                    return Parse(json);
                }
            }
            catch { return new List<Device>(); }
        }

        // Order-tolerant manual mapping (DataContractJsonSerializer is
        // member-order sensitive and silently drops out-of-order fields).
        public static List<Device> Parse(string json)
        {
            var out_ = new List<Device>();
            try
            {
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 8 * 1024 * 1024;
                object root = ser.DeserializeObject(json);
                object[] arr = root as object[];
                if (arr == null) return out_;
                foreach (object o in arr)
                {
                    var m = o as Dictionary<string, object>;
                    if (m == null) continue;
                    var d = new Device();
                    d.InstanceId = Str(m, "instance_id");
                    d.FriendlyName = Str(m, "friendly_name");
                    d.DevClass = Str(m, "dev_class");
                    d.Manufacturer = Str(m, "manufacturer");
                    d.Status = Str(m, "status");
                    if (d.Status == "") d.Status = "OK";
                    d.VenDev = Str(m, "ven_dev");
                    d.DriverVersion = Str(m, "driver_version");
                    d.DriverDate = Str(m, "driver_date");
                    d.DriverProvider = Str(m, "driver_provider");
                    object pc;
                    d.ProblemCode = (m.TryGetValue("problem_code", out pc) && pc is int)
                        ? (int)pc : 0;
                    d.HwId = new List<string>();
                    object hw;
                    if (m.TryGetValue("hwid", out hw) && hw is object[])
                        foreach (object h in (object[])hw)
                            if (h is string) d.HwId.Add((string)h);
                    if (d.InstanceId != "") out_.Add(d);
                }
            }
            catch { }
            return out_;
        }

        private static string Str(Dictionary<string, object> m, string key)
        {
            object v;
            return (m.TryGetValue(key, out v) && v is string) ? (string)v : "";
        }
    }
}
