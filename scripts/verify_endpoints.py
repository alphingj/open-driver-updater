import json
import time
import urllib.request

BASE = "http://127.0.0.1:8901"


def get(path):
    with urllib.request.urlopen(BASE + path, timeout=120) as r:
        return json.loads(r.read())


def post(path, obj):
    data = json.dumps(obj).encode()
    req = urllib.request.Request(BASE + path, data=data,
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read())


print("health:", get("/api/health"))
s = get("/api/scan")
print("scan devices:", len(s["devices"]), "updates:", len(s["updates"]), "arch:", s["arch"])
print("schedule:", get("/api/schedule")["enabled"])
print("job idle:", get("/api/job")["running"] is False)
print("update-all empty ->", post("/api/update-all", {"updates": []}))
print("update-all junk ->", post("/api/update-all", {"updates": [{"update_id": "nope"}]}))
print("unknown route ->", post("/api/nope", {}))
t0 = time.time()
s2 = get("/api/scan")  # should hit 60s cache
print(f"cached rescan: {time.time()-t0:.1f}s")
print("ENDPOINTS OK")
