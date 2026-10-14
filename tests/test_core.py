"""Stdlib unittest suite (no pytest needed): python -m unittest discover -s tests."""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))


class ArchTest(unittest.TestCase):
    def test_canonical(self):
        from odu.arch import canonical_arch
        self.assertEqual(canonical_arch("AMD64"), "x64")
        self.assertEqual(canonical_arch("x86_64"), "x64")
        self.assertEqual(canonical_arch("ARM64"), "arm64")
        self.assertEqual(canonical_arch("aarch64"), "arm64")
        self.assertEqual(canonical_arch("x86"), "x86")

    def test_native_arch_wow64(self):
        from odu import arch
        old = dict(os.environ)
        try:
            os.environ["PROCESSOR_ARCHITEW6432"] = "AMD64"
            os.environ["PROCESSOR_ARCHITECTURE"] = "x86"
            self.assertEqual(arch.native_arch(), "x64")
            os.environ["PROCESSOR_ARCHITEW6432"] = "ARM64"
            self.assertEqual(arch.native_arch(), "arm64")
        finally:
            os.environ.clear()
            os.environ.update(old)

    def test_strict_tags(self):
        from odu.arch import inf_arch_tags, is_arch_compatible
        self.assertEqual(inf_arch_tags("x64"), ("NTAMD64", "NT"))
        self.assertEqual(inf_arch_tags("arm64"), ("NTARM64", "NT"))
        self.assertTrue(is_arch_compatible("[Manufacturer]", "x64"))
        self.assertTrue(is_arch_compatible("[Manufacturer.NTAMD64]", "x64"))
        self.assertFalse(is_arch_compatible("[Manufacturer.NTAMD64]", "arm64"))
        self.assertFalse(is_arch_compatible("[Manufacturer.NTX86]", "x64"))
        self.assertTrue(is_arch_compatible("[Manufacturer.NTARM64]", "arm64"))

    def test_query_candidates(self):
        from odu.catalog import query_candidates
        self.assertEqual(query_candidates("USB\\VID_A&PID_B&REV_C&MI_00"),
                         ["USB\\VID_A&PID_B&REV_C&MI_00", "USB\\VID_A&PID_B",
                          "USB\\VID_A"])
        self.assertEqual(query_candidates(""), [])


class StoreEnvTest(unittest.TestCase):
    def test_lazy_db_path(self):
        from odu import store
        with tempfile.TemporaryDirectory() as d:
            db = os.path.join(d, "t.db")
            os.environ["ODU_DB"] = db
            try:
                store.add_exclusion("HWID1")
                self.assertEqual(store.get_exclusions(), ["HWID1"])
                self.assertTrue(os.path.isfile(db))
                store.remove_exclusion("HWID1")
                self.assertEqual(store.get_exclusions(), [])
            finally:
                del os.environ["ODU_DB"]


class ResolveTest(unittest.TestCase):
    def _devs(self):
        from odu.models import Device
        return [
            Device(instance_id="GPU", friendly_name="GPU",
                   hwid=["PCI\\VEN_10DE&DEV_1234"], status="ERROR", problem_code=28),
            Device(instance_id="GHOST", friendly_name="Ghost USB",
                   hwid=["USB\\VID_1&PID_2"], status="DISCONNECTED", problem_code=45),
            Device(instance_id="VIRT", friendly_name="Volume",
                   hwid=["STORAGE\\Volume"], status="OK", problem_code=0,
                   driver_version="1.0"),
        ]

    def test_skips_ghost_virtual_excluded(self):
        from odu import catalog, store
        with tempfile.TemporaryDirectory() as d:
            os.environ["ODU_DB"] = os.path.join(d, "t.db")
            try:
                ups = catalog.resolve_updates(self._devs(), "x64", live_catalog=False)
                self.assertEqual(len(ups), 1)
                self.assertEqual(ups[0].source, "ms-catalog")
                self.assertEqual(ups[0].vendor, "NVIDIA")
                self.assertTrue(ups[0].vendor_url)
                store.add_exclusion("PCI\\VEN_10DE&DEV_1234")
                self.assertEqual(catalog.resolve_updates(self._devs(), "x64"), [])
            finally:
                del os.environ["ODU_DB"]


class CatalogParseTest(unittest.TestCase):
    GOLDEN = """
    <a id='aaaabbbb-cccc-dddd-eeee-ffffffffffff_link' href= "javascript:void(0);"
     onclick='goToDetails("aaaabbbb-cccc-dddd-eeee-ffffffffffff");'
     class="contentTextItemSpacerNoBreakLink">Acme &amp; Co - Net - 2.0</a>
    <span id="aaaabbbb-cccc-dddd-eeee-ffffffffffff_originalSize">12345</span>
    """

    def test_guid_title_size(self):
        from odu import catalog
        guids = __import__("re").findall(
            r"goToDetails\(\"(" + catalog._GUID + r")\"\)", self.GOLDEN, __import__("re").I)
        self.assertEqual(guids, ["aaaabbbb-cccc-dddd-eeee-ffffffffffff"])
        # exercise the real parser by monkeypatching the fetcher
        orig = catalog._fetch
        catalog._fetch = lambda url, timeout, data=None: self.GOLDEN  # noqa: E731
        try:
            out = catalog._check_ms_catalog_query("ACME")
        finally:
            catalog._fetch = orig
        self.assertEqual(len(out), 1)
        self.assertEqual(out[0]["title"], "Acme & Co - Net - 2.0")
        self.assertEqual(out[0]["size"], 12345)


class InstallerPickTest(unittest.TestCase):
    def _write(self, d, name, text):
        p = os.path.join(d, name)
        with open(p, "w", encoding="utf-8") as f:
            f.write(text)
        return p

    def test_pick_arch(self):
        from odu import installer
        with tempfile.TemporaryDirectory() as d:
            hdr = self._write(d, "hdr.inf",
                              "[Manufacturer.NTX86]\n%M%=M,NTX86\n")
            ent = self._write(d, "ent.inf",
                              "[Manufacturer]\n%M%=Models,NTAMD64\n")
            plain = self._write(d, "plain.inf",
                                "[Manufacturer]\n%M%=Models\n")
            self.assertEqual(installer.pick_inf([hdr, ent], "x64"), ent)
            self.assertEqual(installer.pick_inf([hdr, ent], "x86"), hdr)
            self.assertEqual(installer.pick_inf([hdr], "arm64"), "")
            self.assertEqual(installer.pick_inf([plain], "arm64"), plain)


class PayloadValidationTest(unittest.TestCase):
    def test_guid_filter(self):
        from odu.webapp import _GUID_RE
        self.assertTrue(_GUID_RE.match("13c843c5-150b-4622-b619-2304d557ed66"))
        self.assertFalse(_GUID_RE.match("not-a-guid"))
        self.assertFalse(_GUID_RE.match("13c843c5-150b-4622-b619-2304d557ed66 "))


class SearchUrlTest(unittest.TestCase):
    def test_vendev_preferred(self):
        from odu.catalog import search_url_for
        u = search_url_for("ACP\\DEVTYPE_0001&DEVREV_0000&VEN_1022&DEV_15E2&SUBSYS_10431603&REV_6F")
        self.assertTrue(u.endswith("q=VEN_1022%26DEV_15E2"), u)
        u2 = search_url_for("USB\\VID_35B5&PID_3500&REV_0C02")
        self.assertTrue(u2.endswith("q=VID_35B5%26PID_3500"), u2)

    def test_cut_surrogate(self):
        from odu.catalog import _cut
        self.assertEqual(_cut("abcdef", 64), "abcdef")
        # Python strings are code-point based (no surrogates since 3.3);
        # _cut is a safeguard that must preserve astral chars intact.
        self.assertEqual(_cut("abc\U0001F600def", 4), "abc\U0001F600")

    def test_no_negative_cache(self):
        from odu import catalog
        with tempfile.TemporaryDirectory() as d:
            os.environ["ODU_DB"] = os.path.join(d, "t.db")
            try:
                catalog._check_ms_catalog_query = lambda q, timeout=15: []  # noqa: E731
                try:
                    self.assertEqual(catalog._cached_query("NOPE", 5), [])
                    from odu import store
                    # miss must NOT be cached
                    con = store.connect()
                    try:
                        row = con.execute(
                            "SELECT result FROM catalog_cache WHERE hwid=?",
                            ("q:NOPE",)).fetchone()
                    finally:
                        con.close()
                    self.assertIsNone(row)
                finally:
                    del catalog._check_ms_catalog_query
            finally:
                del os.environ["ODU_DB"]

    def test_find_scanner_arch_order(self):
        from odu import scanner
        with tempfile.TemporaryDirectory() as d:
            os.makedirs(os.path.join(d, "native", "bin", "x64"))
            os.makedirs(os.path.join(d, "native", "bin", "x86"))
            open(os.path.join(d, "native", "bin", "x64", "odu_scan.exe"), "w").close()
            open(os.path.join(d, "native", "bin", "x86", "odu_scan.exe"), "w").close()
            real_file, real_env = scanner.__file__, dict(os.environ)
            try:
                scanner.__file__ = os.path.join(d, "src", "odu", "scanner.py")
                # this host is x64 -> x64 binary preferred
                self.assertIn(os.path.join("bin", "x64", "odu_scan.exe"),
                              scanner.find_scanner())
                # simulate a 32-bit OS -> x86 binary preferred
                os.environ.pop("PROCESSOR_ARCHITEW6432", None)
                os.environ["PROCESSOR_ARCHITECTURE"] = "x86"
                self.assertIn(os.path.join("bin", "x86", "odu_scan.exe"),
                              scanner.find_scanner())
            finally:
                scanner.__file__ = real_file
                os.environ.clear()
                os.environ.update(real_env)


if __name__ == "__main__":
    unittest.main()
