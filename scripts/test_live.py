import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from odu import catalog

HWIDS = [
    "USB\\VID_0E8D&PID_2005&REV_0223&MI_00",
    "USB\\VID_35B5&PID_3500&REV_0C02",
    "MONITOR\\CMN14E5",
    "USB\\VID_0E8D&PID_201C&REV_0223",
]

for h in HWIDS:
    found = catalog.check_ms_catalog(h)
    print("HWID:", h)
    if not found:
        print("  no catalog hits")
    for f in found[:3]:
        print("  -", f["title"], "|", f["url"])
