import os
import sys
import tempfile

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from odu import catalog, installer

GUID = "13c843c5-150b-4622-b619-2304d557ed66"  # Naxiang Net 1.0.0.9
urls = catalog.get_download_urls(GUID)
assert urls, "no download urls"
print("urls:", len(urls), urls[0]["url"][:120])
print("sha256 present:", bool(urls[0]["sha256"]))

tmp = tempfile.mkdtemp(prefix="odu-verify-")
cab = installer.download(urls[0]["url"], tmp)
print("cab:", cab, os.path.getsize(cab), "bytes")
infs = installer.expand_cab(cab, os.path.join(tmp, "x"))
print("infs:", infs)
if infs:
    print("picked:", installer.pick_inf(infs, "x64"))
    print("VERIFY PASS (install step skipped by design)")
