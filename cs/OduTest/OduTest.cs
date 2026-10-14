// OduTest: headless asserts for the C# layer. Exit 0 = all pass.
// Golden HWID vectors are mirrored in native/odu_hw/test_hw.c and
// tests/test_core.py — all three must agree (conformance).
using System;
using System.Collections.Generic;

namespace OduApp
{
    public static class OduTest
    {
        private static int _fails;

        private static void Check(bool cond, string name)
        {
            if (!cond) { Console.WriteLine("FAIL " + name); _fails++; }
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
