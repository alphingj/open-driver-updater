import os

p = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))), "src", "odu", "safety.py")
with open(p, encoding="utf-8") as f:
    lines = f.readlines()
q = chr(39)
bad = "description.replace(" + chr(92) + q + ", " + chr(34) + chr(34) + ")"
good = "description.replace(chr(39), " + chr(34) + chr(34) + ")"
fixed = 0
for i, line in enumerate(lines):
    if "Exception.Message }" in line and "description.replace" in line:
        lines[i] = line.replace(bad, good)
        fixed += 1
with open(p, "w", encoding="utf-8") as f:
    f.writelines(lines)
print("fixed lines:", fixed)
