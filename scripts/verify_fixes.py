import json
import urllib.request
import urllib.error

BASE = "http://127.0.0.1:8901"


def post(path, obj):
    data = json.dumps(obj).encode()
    req = urllib.request.Request(BASE + path, data=data,
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read())


def get(path):
    with urllib.request.urlopen(BASE + path, timeout=60) as r:
        return json.loads(r.read())


import time
time.sleep(3)
print("cancel ->", post("/api/cancel", {}))
print("job ->", {k: get("/api/job")[k] for k in ("running", "total", "done")})
print("rollback junk ->", post("/api/rollback", {"published": "oem1.inf,oem2.inf"}))
print("rollback bad ->", post("/api/rollback", {"published": "../evil"}))
print("health ->", get("/api/health"))
print("ENDPOINTS OK")
