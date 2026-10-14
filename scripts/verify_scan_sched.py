import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from odu import scanner, scheduler

devs = scanner.scan()
errs = [d for d in devs if d.status == "ERROR"]
disc = [d for d in devs if d.status == "DISCONNECTED"]
hwid = sum(1 for d in devs if d.hwid)
print(f"devices={len(devs)} error={len(errs)} disconnected={disc and len(disc)} hwid={hwid}")
assert len(devs) > 100, "scan looks broken"
assert hwid > 100, "HWID enrichment broken"
assert any(d.driver_version for d in devs), "driver enrichment broken"
print("SCAN OK")

on = scheduler.enable_weekly()
print("enable:", on["ok"])
st = scheduler.status()
print("status enabled:", st["enabled"])
off = scheduler.disable()
print("disable:", off["ok"])
st2 = scheduler.status()
print("status after delete:", st2["enabled"])
assert on["ok"] and st["enabled"] and off["ok"] and not st2["enabled"]
print("SCHEDULER OK")
