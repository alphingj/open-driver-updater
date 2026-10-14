import re
import urllib.request

URLS = {
    "nx": "https://www.catalog.update.microsoft.com/Search.aspx?q=USB%5CVID_35B5%26PID_3500",
}

for name, u in URLS.items():
    req = urllib.request.Request(u, headers={"User-Agent": "Mozilla/5.0"})
    h = urllib.request.urlopen(req, timeout=25).read().decode("utf-8", "ignore")
    print(name, "bytes:", len(h))
    print("_link count:", len(re.findall(r"_link", h)))
    guids = re.findall(r'goToDetails\("([0-9a-f-]{36})"\)', h, re.I)
    print("guids:", guids[:5])
    m = re.search(r".{0,80}13c843c5-150b-4622-b619-2304d557ed66_link.{0,200}", h, re.S)
    print("anchor ctx:", repr(m.group(0)) if m else "NO _link anchor")
    anchors = re.findall(r"([0-9a-f-]{36})_link'[^>]*>\s*([^<]{3,200})", h, re.I)
    print("titled anchors:", len(anchors), anchors[:5])
