import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from odu import arch, catalog, scanner

t0 = time.time()
devs = scanner.scan()
print(f"devices={len(devs)} scan={time.time()-t0:.0f}s", flush=True)
t1 = time.time()
ups = catalog.resolve_updates(devs, arch.native_arch(), live_catalog=True)
print(f"updates={len(ups)} live={time.time()-t1:.0f}s", flush=True)
for u in ups:
    print(f"  [{u.source}] {u.device_name} | installed={u.installed_version or 'none'} | {u.available_version[:70]}",
          flush=True)
with open(os.path.join(os.path.dirname(__file__), "live_result.json"), "w") as f:
    json.dump({"devices": len(devs), "updates": [u.to_dict() for u in ups]}, f)
print("LIVE DONE", flush=True)
