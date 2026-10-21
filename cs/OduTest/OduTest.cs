// OduTest: headless asserts for the C# layer. Exit 0 = all pass.
// Golden HWID vectors are mirrored in native/odu_hw/test_hw.c and
// tests/test_core.py — all three must agree (conformance).
using System;
using System.Collections.Generic;
using System.IO;

namespace OduApp
{
    public static class OduTest
    {
        private static int _fails;

        private static void Check(bool cond, string name)
        {
            if (!cond) { Console.WriteLine("FAIL " + name); _fails++; }
        }

        private static string Join(int[] v)
        {
            string s = "";
            for (int i = 0; i < v.Length; i++)
                s += (i > 0 ? "." : "") + v[i];
            return s;
        }

        public static int Run()
        {
            // ArchUtil (mirrors C odu_canonical_arch + INF tags)
            Check(ArchUtil.Canonical("AMD64") == "x64", "arch-amd64");
            Check(ArchUtil.Canonical("ARM64") == "arm64", "arch-arm64");
            Check(ArchUtil.InfCompatible("[Manufacturer.NTAMD64]", "x64"), "inf-x64-ok");
            Check(!ArchUtil.InfCompatible("[Manufacturer.NTAMD64]", "arm64"), "inf-substring-trap");
            Check(!ArchUtil.InfCompatible("[Manufacturer.NTX86]", "x64"), "inf-x86-on-x64");
            Check(ArchUtil.InfCompatible("[Manufacturer]", "arm64"), "inf-plain");
            Check(ArchUtil.InfCompatible("[Manufacturer]\n%M%=M,NTAMD64", "x64"), "inf-entry-form");
            // UTF-16-shaped content must not false-positive (BOM-less ASCII here
            // stands in for decoded text; real UTF-16 goes through ReadInfText)
            Check(!ArchUtil.InfCompatible("[Manufacturer.NTX86]", "x64"), "inf-x86-strict");

            // Catalog query shapes (mirrors Python query_candidates)
            List<string> c = Catalog.QueryCandidates(
                "ACP\\DEVTYPE_0001&DEVREV_0000&VEN_1022&DEV_15E2&SUBSYS_10431603&REV_6F");
            Check(c.Contains("VEN_1022&DEV_15E2"), "cand-vendev");
            Check(Catalog.SearchUrlFor(
                "ACP\\DEVTYPE_0001&DEVREV_0000&VEN_1022&DEV_15E2&SUBSYS_10431603&REV_6F")
                .EndsWith("q=VEN_1022%26DEV_15E2"), "url-vendev");
            Check(Catalog.VenOf("USB\\VID_0E8D&PID_2005") == "VEN_0E8D", "ven-vid");
            Check(Catalog.IsSkipped("SWD\\FOO"), "skip-swd");
            Check(!Catalog.IsSkipped("PCI\\VEN_1022&DEV_15E2"), "skip-pci-no");

            // Surrogate-safe cut
            Check(Catalog.Cut("abcdef", 64) == "abcdef", "cut-short");
            Check(Catalog.Cut("abc\U0001F600def", 4) == "abc", "cut-surrogate");

            // Relevance gate (mirrors Python _specific_query/title_matches_device)
            Check(!Catalog.SpecificQuery("ACPI\\VEN_PNP"), "rel-generic");
            Check(Catalog.SpecificQuery("VEN_1022&DEV_15E2"), "rel-pair");
            Check(Catalog.SpecificQuery("PCI\\VEN_1022&DEV_15E2&SUBSYS_X"), "rel-full");
            var dma = new Device();
            dma.FriendlyName = "Direct memory access controller";
            dma.DevClass = "System";
            dma.Manufacturer = "(Standard system devices)";
            Check(!Catalog.TitleMatchesDevice("Keyboard Filter Driver - Keyboard - 22.5.23.1", dma), "rel-reject");
            var rtk = new Device();
            rtk.FriendlyName = "Realtek Audio Effects Component";
            rtk.DevClass = "AudioProcessingObject";
            rtk.Manufacturer = "Realtek";
            Check(Catalog.TitleMatchesDevice("Realtek AudioProcessingObject Driver Update (13.6709.2609.932)", rtk), "rel-accept");

            // Version comparison (mirrors Python catalog_version/is_newer)
            Check(Join(Catalog.CatalogVersion("Naxiang - Net - 1.0.0.9")) == "1.0.0.9", "ver-trailing");
            Check(Join(Catalog.CatalogVersion("AMD System Driver Update (7.2610.1.1)")) == "7.2610.1.1", "ver-paren");
            Check(Join(Catalog.CatalogVersion("Android - Net - 3/30/2016 12:00:00 AM - 1.0.0.1")) == "1.0.0.1", "ver-date");
            Check(Catalog.CatalogVersion("No version here").Length == 0, "ver-none");
            Check(Catalog.IsNewer(new int[] { 1, 0, 0, 9 }, "1.0.0.1"), "newer-yes");
            Check(!Catalog.IsNewer(new int[] { 1, 0, 0, 9 }, "1.0.0.9"), "newer-same");
            Check(!Catalog.IsNewer(new int[] { 7, 2610, 1, 1 }, "32.0.21030.31"), "newer-scheme");

            // ParseSearch golden HTML
            string golden = "<a id='aaaabbbb-cccc-dddd-eeee-ffffffffffff_link' href= \"javascript:void(0);\" " +
                "onclick='goToDetails(\"aaaabbbb-cccc-dddd-eeee-ffffffffffff\");' >" +
                "Acme &amp; Co - Net - 2.0</a>" +
                "<span id=\"aaaabbbb-cccc-dddd-eeee-ffffffffffff_originalSize\">12345</span>";
            List<CatalogHit> hits = Catalog.ParseSearch(golden);
            Check(hits.Count == 1, "parse-count");
            if (hits.Count == 1)
            {
                Check(hits[0].Title == "Acme & Co - Net - 2.0", "parse-title");
                Check(hits[0].Size == 12345, "parse-size");
            }

            // Store cache round-trip incl. quotes/braces in values
            string k = "test:key-" + Guid.NewGuid().ToString("N");
            string v = "[{\"id\":\"x\",\"title\":\"A {bracket} \\\"q\\\"\",\"size\":1,\"url\":\"u\"}]";
            Store.CachePut(k, v);
            Check(Store.CacheGet(k) == v, "cache-roundtrip");

            // Rollback allow-list (mirrors Python odu_installs table)
            string oemKey = "oem199998.inf";
            Store.ForgetInstall(oemKey);
            Check(!Store.IsRecorded(oemKey), "rb-absent");
            Store.RecordInstall(oemKey, "ID1", "Dev");
            Check(Store.IsRecorded("OEM199998.INF"), "rb-recorded");
            Store.RecordInstall("not-an-oem", "ID1", "Dev");
            Check(!Store.IsRecorded("not-an-oem"), "rb-reject");
            // previous-driver metadata survives the round trip
            Store.RecordInstall(oemKey, "ID1", "Dev", "OEM5.INF", "1.2.3", @"C:\b");
            Store.InstallRow row = Store.InstallInfo("oem199998.inf");
            Check(row != null && row.PrevOem == "oem5.inf" && row.PrevVersion == "1.2.3"
                  && row.PrevPath == @"C:\b", "rb-prev-roundtrip");
            // older lines (4 fields) still parse with empty prev fields
            Check(Store.InstallInfo("oem199990.inf") == null, "rb-info-absent");
            Store.ForgetInstall(oemKey);
            Check(!Store.IsRecorded(oemKey), "rb-forgot");
            Check(Ops.ParseCurrentInf("Driver Name: oem15.inf\r\nExtension Driver Names: oem16.inf")
                  == "oem15.inf", "rb-parse-current");
            Check(Ops.ParseCurrentInf("in-box only") == "", "rb-parse-current-empty");
            Check(Ops.RestorePointUnavailable(
                  "FAIL: System Restore is disabled on the system drive"), "rp-disabled");
            Check(!Ops.RestorePointUnavailable("restore point created"), "rp-enabled");

            // Download cache: stale CABs dropped, fresh kept, non-cab untouched.
            string dl = Path.Combine(Path.GetTempPath(),
                                     "odu-test-dl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dl);
            string oldCab = Path.Combine(dl, "old.cab");
            string newCab = Path.Combine(dl, "new.cab");
            string note = Path.Combine(dl, "note.txt");
            File.WriteAllText(oldCab, "xxxxxxxxxx");
            File.WriteAllText(newCab, "y");
            File.WriteAllText(note, "z");
            File.SetLastWriteTimeUtc(oldCab, DateTime.UtcNow.AddDays(-30));
            Ops.CleanupDownloads(dl, 7, 2L * 1024 * 1024 * 1024);
            Check(!File.Exists(oldCab) && File.Exists(newCab) && File.Exists(note),
                  "dl-age");
            // 11 bytes of CABs against a 5-byte cap -> oldest CAB goes.
            File.WriteAllText(oldCab, "xxxxxxxxxx");
            File.SetLastWriteTimeUtc(oldCab, DateTime.UtcNow.AddDays(-1));
            Ops.CleanupDownloads(dl, 7, 5);
            Check(!File.Exists(oldCab) && File.Exists(newCab), "dl-cap");
            try { Directory.Delete(dl, true); } catch { }

            // NativeScan.Parse tolerance
            var devs = NativeScan.Parse("[{\"instance_id\":\"X\",\"hwid\":[\"A\"],\"problem_code\":7}]");
            Check(devs.Count == 1 && devs[0].Status == "OK" && devs[0].ProblemCode == 7, "parse-minimal");
            devs = NativeScan.Parse("garbage{{{");
            Check(devs.Count == 0, "parse-garbage");

            // Resolve: ghosts + virtuals skipped, vendor attached
            var list = new List<Device>();
            var g = new Device();
            g.InstanceId = "GPU"; g.FriendlyName = "GPU";
            g.HwId = new List<string>(new string[] { "PCI\\VEN_10DE&DEV_1234" });
            g.Status = "ERROR"; g.ProblemCode = 28;
            list.Add(g);
            var ghost = new Device();
            ghost.InstanceId = "GHOST"; ghost.HwId = new List<string>(new string[] { "USB\\VID_1&PID_2" });
            ghost.Status = "DISCONNECTED"; ghost.ProblemCode = 45;
            list.Add(ghost);
            List<DriverUpdate> ups = Catalog.Resolve(list, new List<string>(), false);
            Check(ups.Count == 1 && ups[0].Vendor == "NVIDIA", "resolve-filter-vendor");

            Console.WriteLine(_fails == 0 ? "ODUTEST OK" : ("ODUTEST FAILURES=" + _fails));
            return _fails == 0 ? 0 : 1;
        }
    }
}
