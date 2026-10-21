// CrashLog: last-resort diagnostics for the GUI. Every method is best-effort
// and swallows its own errors so logging can never cause a second failure.
using System;
using System.IO;
using System.Text;

namespace OduApp
{
    public static class CrashLog
    {
        public const long MaxBytes = 512 * 1024;

        public static string FilePath()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Open Driver Updater");
                Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "crash.log");
            }
            catch { return ""; }
        }

        public static string Describe(Exception e)
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).Append("Z ");
            if (e == null) sb.Append("unknown error");
            else
            {
                sb.Append(e.GetType().FullName).Append(": ").Append(e.Message);
                if (e.StackTrace != null) sb.AppendLine().Append(e.StackTrace);
            }
            sb.AppendLine();
            return sb.ToString();
        }

        public static void Write(Exception e)
        {
            Write(FilePath(), e);
        }

        public static void Write(string path, Exception e)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                // Keep the log bounded: a repeating failure must not fill the disk.
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.Delete(path);
                File.AppendAllText(path, Describe(e) + Environment.NewLine);
            }
            catch { }
        }
    }
}
