import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from odu import safety, components, installer

print("reboot:", safety.reboot_pending())
for c in components.check():
    print("component:", c["name"], c["status"], "|", c["url"][:60])
print("rollback guard:", installer.rollback_driver("INVALID!!"))
print("rollback guard2:", installer.rollback_driver("oem12.inf")["ok"] is False or True)
