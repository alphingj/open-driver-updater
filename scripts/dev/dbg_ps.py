import os
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from odu import scanner

raw = scanner._run_ps()
print("raw len:", len(raw))
print("has HW sentinel:", "__ODU_HW__" in raw)
print("has DRV sentinel:", "__ODU_DRV__" in raw)
print("head:", raw[:300].replace("\r", "\\r").replace("\n", "\\n"))
devs = scanner.scan_powershell()
print("parsed:", len(devs))
