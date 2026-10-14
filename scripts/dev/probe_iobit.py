import re

with open(r"C:\Users\alphi\AppData\Local\Temp\db-setup.exe", "rb") as f:
    data = f.read()

text = data.decode("ascii", "ignore")
uni = data.decode("utf-16-le", "ignore")
blob = text + "\n" + uni

print("size:", len(data))
for pat in ["Inno Setup", "NSIS", "7zSFX", "IObit", "driver-booster",
            "db.iobit", "update.iobit", "http://", "https://"]:
    print(f"{pat}: {blob.count(pat)}")

urls = sorted(set(re.findall(r"https?://[a-zA-Z0-9.\-_/]+", blob)))
print("--- urls (%d) ---" % len(urls))
seen = set()
for u in urls:
    key = u[:80]
    if key in seen:
        continue
    seen.add(key)
    if any(k in u for k in ["iobit", "update", "driver", "stat", "track"]):
        print(" ", u[:160])
