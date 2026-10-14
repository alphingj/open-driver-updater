import os
import re
import sys
import urllib.parse
import urllib.request

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from odu import catalog

HWIDS = [
    "USB\\VID_35B5&PID_3500&REV_0C02",
    "USB\\VID_35B5&PID_3500",
    "MONITOR\\CMN14E5",
]

for h in HWIDS:
    url = catalog.catalog_search_url(h)
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    html = urllib.request.urlopen(req, timeout=25).read().decode("utf-8", "ignore")
    guids = re.findall(r'goToDetails\("([0-9a-f-]{36})"\)', html, re.I)
    print("HWID:", h)
    print("  url:", url)
    print("  bytes:", len(html), "guids:", len(guids))
    m = re.search(r"searchDuration\">([^<]+)<", html)
    print("  count label:", m.group(1).strip() if m else "?")
