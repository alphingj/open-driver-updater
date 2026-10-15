import json
import urllib.request

BASE = "http://127.0.0.1:8901"


def get(path):
    with urllib.request.urlopen(BASE + path, timeout=120) as r:
        return json.loads(r.read())


d = get("/api/drivers")
print("published drivers:", len(d["drivers"]))
print("sample:", d["drivers"][0] if d["drivers"] else None)
p = get("/api/procs")
print("procs:", len(p["procs"]), "top:", p["procs"][0] if p["procs"] else None)
print("ENDPOINTS OK")
