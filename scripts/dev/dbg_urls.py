import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from odu import catalog

for h in ["ACP\\DEVTYPE_0001&DEVREV_0000&VEN_1022&DEV_15E2&SUBSYS_10431603&REV_6F",
          "ACPI\\PNP0C0C",
          "MONITOR\\CMN14E5",
          "USB\\VID_35B5&PID_3500&REV_0C02"]:
    print(repr(h), "->", catalog.search_url_for(h))
