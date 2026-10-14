import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

os.environ["ODU_DB"] = os.path.join(os.path.dirname(__file__), "test_e2e.db")
if os.path.exists(os.environ["ODU_DB"]):
    os.remove(os.environ["ODU_DB"])

from odu import arch, catalog, scanner, store

t0 = time.time()
devs = scanner.scan()
ups = catalog.resolve_updates(devs, arch.canonical_arch(), live_catalog=True)
print(f"devices={len(devs)} updates={len(ups)} live_hits="
      f"{sum(1 for u in ups if u.available_version not in ('Check Catalog', 'Update available'))} "
      f"in {time.time()-t0:.1f}s arch={arch.canonical_arch()}")
for u in ups:
    print(f"  [{u.source}] {u.device_name} | {u.hwid} | {u.available_version[:60]}")

# exclusion round-trip
store.add_exclusion(ups[0].hwid)
ups2 = catalog.resolve_updates(devs, arch.canonical_arch())
assert len(ups2) == len(ups) - sum(1 for u in ups if u.hwid == ups[0].hwid), "exclusion failed"
store.remove_exclusion(ups[0].hwid)
ups3 = catalog.resolve_updates(devs, arch.canonical_arch())
assert len(ups3) == len(ups), "un-exclude failed"
print(f"exclusion round-trip OK ({len(ups)} -> {len(ups2)} -> {len(ups3)})")

os.remove(os.environ["ODU_DB"])
print("E2E PASS")
