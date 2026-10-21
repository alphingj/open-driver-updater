// SingleInstance: one GUI per user session. Two windows would be able to start
// two driver jobs at once (restore points, backups, installs), so a second
// launch exits instead. --selftest bypasses this check on purpose.
using System;
using System.Threading;

namespace OduApp
{
    public static class SingleInstance
    {
        private static Mutex _held;

        public static bool Acquire(string name)
        {
            try
            {
                bool created;
                var m = new Mutex(true, @"Local\" + name, out created);
                if (!created)
                {
                    try { m.Dispose(); } catch { }
                    return false;
                }
                _held = m;  // keep the handle alive for the process lifetime
                return true;
            }
            catch
            {
                // A broken mutex must never stop the app from starting.
                return true;
            }
        }

        public static void Release()
        {
            try
            {
                if (_held != null)
                {
                    _held.ReleaseMutex();
                    _held.Dispose();
                }
            }
            catch { }
            _held = null;
        }
    }
}
