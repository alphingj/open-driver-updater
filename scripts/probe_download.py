import json
import re
import urllib.parse
import urllib.request

GUID = "13c843c5-150b-4622-b619-2304d557ed66"
payload = json.dumps([{"size": 109559, "languages": "", "uidInfo": GUID, "updateID": GUID}])
data = urllib.parse.urlencode({
    "updateIDs": payload, "updateIDsBlockedForImport": "", "contentImport": "",
    "sku": "", "serverName": "", "ssl": "", "portNumber": "",
    "version": "", "protocol": "",
}).encode()
req = urllib.request.Request(
    "https://www.catalog.update.microsoft.com/DownloadDialog.aspx",
    data=data, headers={"User-Agent": "Mozilla/5.0",
                        "Content-Type": "application/x-www-form-urlencoded"})
h = urllib.request.urlopen(req, timeout=30).read().decode("utf-8", "ignore")
idxs = [m.start() for m in re.finditer(r"downloadInformation", h)][:4]
print("total occurrences:", len(re.findall(r"downloadInformation", h)))
for i in idxs:
    print("---- ctx ----")
    print(h[max(0, i-200):i+600].replace("\r", "")[:800])
    print()
