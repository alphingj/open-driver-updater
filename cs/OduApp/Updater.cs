// One-click queue: backup gates everything, then per-driver CAB install.
using System;
using System.Collections.Generic;
using System.IO;

namespace OduApp
{
    public static class Updater
    {
        private static volatile bool _cancel;

        public static void Cancel() { _cancel = true; }

        public static List<JobItem> RunAll(List<DriverUpdate> items, string arch,
                                           Action<string> progress)

        {
            var results = new List<JobItem>();
            _cancel = false;
            if (progress != null) progress("Creating system restore point…");
            OpResult restore = Ops.RestorePoint();
            var rp = new JobItem();
            rp.Device = "System restore point";
            rp.HwId = "";
            rp.Ok = restore.Ok;
            rp.Detail = restore.Ok ? "created" : restore.Log;
            results.Add(rp);
            if (progress != null) progress("Backing up existing drivers…");
            OpResult backup = Ops.BackupAll(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                             "odu-backup"));
            var b = new JobItem();
            b.Device = "Pre-update backup";
            b.HwId = "";
            b.Ok = backup.Ok;
            b.Detail = backup.Path != "" ? backup.Path : backup.Log;
            results.Add(b);
            if (!backup.Ok)
            {
                var stop = new JobItem();
                stop.Device = "Update queue";
                stop.HwId = "";
                stop.Ok = false;
                stop.Detail = "stopped: backup failed, nothing was installed";
                results.Add(stop);
                return results;
            }
            string dl = Path.Combine(Path.GetTempPath(), "odu-dl");
            try { Directory.CreateDirectory(dl); } catch { }
            foreach (DriverUpdate u in items)
            {
                if (_cancel)
                {
                    var c = new JobItem();
                    c.Device = "Update queue";
                    c.HwId = "";
                    c.Ok = false;
                    c.Detail = "cancelled by user; remaining drivers untouched";
                    results.Add(c);
                    break;
                }
                var r = new JobItem();
                r.Device = u.DeviceName;
                r.HwId = u.HwId;
                r.Ok = false;
                r.Detail = "";
                r.Published = "";
                try
                {
                    if (progress != null) progress("Resolving " + u.DeviceName + "…");
                    List<Catalog.CabFile> urls = Catalog.DownloadUrls(u.UpdateId, u.Size);
                    if (urls.Count == 0)
                    {
                        r.Detail = "no CAB published for this update";
                    }
                    else
                    {
                        bool okAll = true;
                        string logs = "";
                        string pubs = "";
                        foreach (Catalog.CabFile f in urls)
                        {
                            if (progress != null) progress("Downloading " + u.DeviceName + "…");
                            string cab = Path.Combine(dl, CabName(f.Url));
                            Action<string> prog = progress;
                            string dev = u.DeviceName;
                            if (!Ops.Download(f.Url, cab, f.Sha256B64, delegate(int pct)
                            {
                                if (prog != null) prog("Downloading " + dev + "… " + pct + "%");
                            }))
                            {
                                okAll = false;
                                logs += "download failed; ";
                                continue;
                            }
                            if (progress != null) progress("Installing " + u.DeviceName + "…");
                            OpResult inst = Ops.InstallCab(cab, arch);
                            // Keep failed CABs for offline retry/debug; drop successes.
                            if (inst.Ok) { try { File.Delete(cab); } catch { } }
                            string pub = Ops.PublishedName(inst.Log);
                            if (pub != "") pubs += (pubs == "" ? "" : ",") + pub;
                            okAll = okAll && inst.Ok;
                            logs += (inst.Path != "" ? inst.Path + " :: " : "") + inst.Log + " ||| ";
                        }
                        r.Ok = okAll;
                        r.Published = pubs;
                        if (okAll && pubs != "")
                        {
                            foreach (string p in pubs.Split(','))
                                Store.RecordInstall(p, u.InstanceId, u.DeviceName);
                        }
                        r.Detail = logs.Length > 2000 ? logs.Substring(logs.Length - 2000) : logs;
                    }
                }
                catch (Exception e)
                {
                    r.Detail = e.Message.Length > 300 ? e.Message.Substring(0, 300) : e.Message;
                }
                results.Add(r);
            }
            return results;
        }

        private static string CabName(string url)
        {
            int q = url.IndexOf('?');
            if (q >= 0) url = url.Substring(0, q);
            int s = url.LastIndexOf('/');
            string n = s >= 0 ? url.Substring(s + 1) : "driver.cab";
            if (n.Length > 80) n = n.Substring(n.Length - 80);
            if (!n.EndsWith(".cab", StringComparison.OrdinalIgnoreCase)) n += ".cab";
            return n;
        }
    }
}
