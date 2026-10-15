import json
import time
import urllib.request

BASE = "http://127.0.0.1:8901"


def get(path, timeout=120):
    with urllib.request.urlopen(BASE + path, timeout=timeout) as r:
        return json.loads(r.read())


time.sleep(3)
h = get("/api/health", 15)
print("server:", h["version"], h["arch"])
t0 = time.time()
s = get("/api/scan?live=1", 600)
print(f"devices={len(s['devices'])} updates={len(s['updates'])} took={time.time()-t0:.0f}s")
high = [u for u in s["updates"] if u.get("confidence", "high") == "high"]
rev = [u for u in s["updates"] if u.get("confidence", "high") != "high"]
print(f"ready={len(high)} review={len(rev)}")
for u in high:
    print(f"  READY {u['device_name'][:50]} | installed={u['installed_version'] or 'none'} | {u['available_version'][:60]}")
for u in rev:
    print(f"  REVIEW {u['device_name'][:50]} | installed={u['installed_version'] or 'none'} | {u['available_version'][:60]}")
