import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from odu import safety, components, installer

print("reboot:", safety.reboot_pending())
for c in components.check():
    print("component:", c["name"], c["status"], "|", c["url"][:60])
print("rollback guard:", installer.rollback_driver("INVALID!!"))
# Never pass a real oemNN.inf here: the primitive deletes packages. A name
# without ".inf" is rejected before pnputil runs, which is what we test.
print("rollback guard2:", installer.rollback_driver("oem12")["ok"])
