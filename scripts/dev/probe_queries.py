import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from odu import catalog

QUERIES = [
    "ACP\\DEVTYPE_0001&DEVREV_0000&VEN_1022&DEV_15E2&SUBSYS_10431603&REV_6F",
    "ACP\\DEVTYPE_0001&DEVREV_0000",
    "VEN_1022&DEV_15E2",
    "DEV_15E2",
    "AMD ACP HDA",
    "USB\\VID_0E8D&PID_2005",
    "VID_0E8D&PID_2005",
]

for q in QUERIES:
    try:
        r = catalog._check_ms_catalog_query(q)
    except Exception as e:
        print(f"{q!r:70} ERROR {e}")
        continue
    print(f"{q!r:70} hits={len(r)}" + (f" e.g. {r[0]['title'][:60]}" if r else ""))
