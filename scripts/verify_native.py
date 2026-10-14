import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

exe = os.path.join(os.path.dirname(__file__), "..", "native", "bin", "x64", "odu_scan.exe")
t0 = __import__("time").time()
out = subprocess.run([exe], capture_output=True, timeout=60)
dt = __import__("time").time() - t0
devs = json.loads(out.stdout.decode("utf-8", "ignore"))
print(f"native scan: {len(devs)} devices in {dt:.1f}s")
errs = [d for d in devs if d["status"] == "ERROR"]
disc = [d for d in devs if d["status"] == "DISCONNECTED"]
print(f"error={len(errs)} disconnected={len(disc)} hwid={sum(1 for d in devs if d['hwid'])}")
assert len(devs) > 100 and out.returncode == 0

from odu import scanner
py = scanner.scan()
pn = {d["instance_id"] for d in devs}
pp = {d.instance_id for d in py}
print(f"py-scan: {len(py)} devices; overlap={len(pn & pp)}")
assert len(pn & pp) > 100, "native/py device sets diverge"
vd = sum(1 for d in devs if d["ven_dev"])
print(f"ven_dev populated: {vd}")
print("NATIVE SCAN OK")
