import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FILES = ["pyproject.toml", "src/odu/__init__.py", "cs/OduApp/Program.cs",
         "cs/OduApp/AssemblyInfo.cs", "installer/setup.iss",
         "native/odu_scan/version.rc", "README.md"]
for rel in FILES:
    p = os.path.join(ROOT, rel)
    with open(p, encoding="utf-8") as f:
        text = f.read()
    new = text.replace("0.4.0", "0.5.0")
    if new != text:
        with open(p, "w", encoding="utf-8") as f:
            f.write(new)
        print("bumped", rel)
