import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))
from odu.arch import canonical_arch, inf_arch_tags, is_arch_compatible
from odu.models import Device
from odu import catalog


def test_arch():
    assert canonical_arch("AMD64") == "x64"
    assert canonical_arch("ARM64") == "arm64"
    assert canonical_arch("x86") == "x86"
    assert "NTARM64" in inf_arch_tags("arm64")
    assert "NTAMD64" in inf_arch_tags("x64")
    assert is_arch_compatible("[Manufacturer.NTAMD64]", "x64")


def test_resolve_skips_excluded(tmp_path, monkeypatch):
    monkeypatch.setenv("ODU_DB", str(tmp_path / "t.db"))
    devs = [Device(instance_id="X", friendly_name="GPU", hwid=["PCI\\VEN_10DE&DEV_1234"],
                   status="ERROR", problem_code=28)]
    ups = catalog.resolve_updates(devs, "x64", live_catalog=False)
    assert len(ups) == 1 and ups[0].source == "ms-catalog"
    from odu import store
    store.add_exclusion("PCI\\VEN_10DE&DEV_1234")
    assert catalog.resolve_updates(devs, "x64", live_catalog=False) == []
