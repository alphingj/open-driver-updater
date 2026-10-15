// Entry point: GUI by default, --selftest* for headless verification.
using System;
using System.Collections.Generic;
using System.Windows;

namespace OduApp
{
    public static class AppVersion
    {
        public const string Value = "0.7.2";
    }

    public static class Program
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [STAThread]
        public static int Main(string[] args)
        {
            foreach (string a in args)
            {
                if (a == "--selftest" || a == "--selftest-scan")
                {
                    try { AllocConsole(); } catch { }
                    return SelfTest();
                }
                if (a == "--help" || a == "-h" || a == "/?")
                {
                    try { AllocConsole(); } catch { }
                    try
                    {
                        System.Console.WriteLine(
                            "Open Driver Updater\n" +
                            "  OduApp.exe                  start the desktop UI\n" +
                            "  OduApp.exe --selftest       headless scan+resolve check\n" +
                            "  OduApp.exe --help           this text");
                    }
                    catch { }
                    return 0;
                }
            }
            var app = new Application();
            app.Run(new MainWindow());
            return 0;
        }

        private static int SelfTest()
        {
            string log = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "odu-selftest.txt");
            try
            {
                List<Device> devs = NativeScan.Scan(120000);
                string msg = "devices=" + devs.Count + " arch=" + ArchUtil.NativeArch()
                    + " scanner=" + NativeScan.FindScanner() + "\r\n";
                if (devs.Count > 0)
                {
                    Device d0 = devs[0];
                    msg += "sample status=" + d0.Status + " problem=" + d0.ProblemCode
                        + " drv=" + d0.DriverVersion + " hwids=" + (d0.HwId != null ? d0.HwId.Count : -1)
                        + " id=" + d0.InstanceId + "\r\n";
                }
                if (devs.Count < 10) { System.IO.File.WriteAllText(log, "FAIL " + msg); return 1; }
                List<DriverUpdate> ups = Catalog.Resolve(devs, Store.Exclusions(), false);
                msg += "updates=" + ups.Count + "\r\n";
                foreach (DriverUpdate u in ups)
                    msg += "  " + u.DeviceName + " | " + u.HwId + "\r\n";
                System.IO.File.WriteAllText(log, "OK " + msg);
                try { System.Console.WriteLine(msg); } catch { }
                return 0;
            }
            catch (Exception e)
            {
                try { System.IO.File.WriteAllText(log, "FAIL " + e); } catch { }
                return 2;
            }
        }
    }
}
