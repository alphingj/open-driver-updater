import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
exe = os.path.join(ROOT, "native", "bin", "x64", "odu_scan.exe")
native = {d["instance_id"]: d for d in
          json.loads(subprocess.run([exe], capture_output=True,
                                    timeout=60).stdout.decode("utf-8", "ignore"))}

from odu import scanner
py = {d.instance_id: d for d in scanner.scan()}

mis = 0
for iid, n in native.items():
    p = py.get(iid)
    if p and (n["status"] != p.status or n["problem_code"] != p.problem_code):
        mis += 1
        if mis <= 15:
            print(f"{iid[:60]}\n  native={n['status']}/{n['problem_code']}  py={p.status}/{p.problem_code}")
print("mismatches:", mis, "of", len(native))
