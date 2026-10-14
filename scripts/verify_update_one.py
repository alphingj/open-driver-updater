import json
import time
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


time.sleep(3)
print("junk ->", post("/api/update-one", {"update": {"update_id": "nope"}}))
print("empty ->", post("/api/update-one", {}))
print("health ->", json.load(urllib.request.urlopen(BASE + "/api/health", timeout=15))["version"])
