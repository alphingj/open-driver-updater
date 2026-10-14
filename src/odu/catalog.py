"""Driver source resolver. Open payloads only, fetched on demand, never bundled.

Priority: windows-update -> ms-catalog -> vendor-direct -> sdio -> local.
"""
from __future__ import annotations
import re
import urllib.parse
import urllib.request
from .arch import canonical_arch
from .models import Device, DriverUpdate
from . import store as store_mod

MS_CATALOG_SEARCH = "https://www.catalog.update.microsoft.com/Search.aspx?q="

VENDOR_HINTS: list[tuple[str, str, str]] = [
    ("VEN_8086", "Intel", "https://www.intel.com/content/www/us/en/download-center.html"),
    ("VEN_10DE", "NVIDIA", "https://www.nvidia.com/drivers"),
    ("VEN_1002", "AMD", "https://www.amd.com/en/support/downloads.html"),
    ("VEN_10EC", "Realtek", "https://www.realtek.com/Download/Index"),
    ("VEN_14E4", "Broadcom", "https://www.broadcom.com/support/download-search"),
]


def _ven(hwid: str) -> str:
    m = re.search(r"VEN_([0-9A-Fa-f]{4})", hwid or "")
    return ("VEN_" + m.group(1).upper()) if m else ""


def vendor_hint(hwid: str) -> dict:
    for ven, name, url in VENDOR_HINTS:
        if _ven(hwid) == ven:
            return {"vendor": name, "url": url}
    return {"vendor": "", "url": ""}


def catalog_search_url(hwid: str) -> str:
    return MS_CATALOG_SEARCH + urllib.parse.quote(hwid)


def query_candidates(hwid: str) -> list[str]:
    """Full HWID first, then progressively shorter forms.

    MS Catalog search is near-literal: 'USB\\VID_x&PID_y&REV_z' often scores
    zero while 'USB\\VID_x&PID_y' hits. Same for PCI SUBSYS/REV suffixes.
    """
    hwid = (hwid or "").strip()
    if not hwid:
        return []
    cands = [hwid]
    if "&" in hwid:
        head, rest = hwid.split("\\", 1) if "\\" in hwid else ("", hwid)
        parts = rest.split("&")
        for n in (2, 1):
            if len(parts) > n:
                short = head + "\\" + "&".join(parts[:n]) if head else "&".join(parts[:n])
                if short not in cands:
                    cands.append(short)
    return cands


def check_ms_catalog(hwid: str, timeout: int = 15) -> list[dict]:
    """Best-effort live lookup of Microsoft Update Catalog (HTML, may change)."""
    for q in query_candidates(hwid):
        found = _check_ms_catalog_query(q, timeout)
        if found:
            return found
    return []


def _check_ms_catalog_query(query: str, timeout: int = 15) -> list[dict]:
    url = MS_CATALOG_SEARCH + urllib.parse.quote(query)
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=timeout) as r:
            html = r.read().decode("utf-8", "ignore")
    except Exception:
        return []
    # Catalog embeds results as: onclick='goToDetails("guid");' + <a id='guid_link'>Title</a>
    guids = re.findall(r"goToDetails\(&quot;([0-9a-f\-]{36})&quot;\)", html, re.I)
    if not guids:
        guids = re.findall(r"goToDetails\(\"([0-9a-f\-]{36})\"\)", html, re.I)
    titles: dict[str, str] = {}
    for m in re.finditer(r"([0-9a-f-]{36})_link'[^>]*>\s*([^<]{3,200})", html, re.I):
        titles[m.group(1).lower()] = re.sub(r"\s+", " ", m.group(2)).strip()
    out = []
    for guid in guids[:10]:
        title = titles.get(guid.lower(), "")
        if not title:
            continue
        out.append({
            "id": guid,
            "title": title,
            "url": f"https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid={guid}",
        })
    return out


def resolve_updates(devices: list[Device], arch: str | None = None,
                    live_catalog: bool = False) -> list[DriverUpdate]:
    arch = canonical_arch(arch)
    excluded = set(store_mod.get_exclusions())
    updates: list[DriverUpdate] = []
    for dev in devices:
        if not dev.hwid:
            continue
        hwid = dev.hwid[0]
        if hwid in excluded or dev.instance_id in excluded:
            continue
        needs = dev.problem_code != 0 or dev.status == "ERROR" or not dev.driver_version
        hint = vendor_hint(hwid)
        if live_catalog and needs:
            found = check_ms_catalog(hwid)
            if found:
                f = found[0]
                updates.append(DriverUpdate(
                    hwid=hwid, device_name=dev.friendly_name or dev.instance_id,
                    instance_id=dev.instance_id, installed_version=dev.driver_version,
                    available_version=f["title"][:64], source="ms-catalog",
                    download_url=f["url"], arch=arch, title=f["title"]))
                continue
        if needs:
            updates.append(DriverUpdate(
                hwid=hwid, device_name=dev.friendly_name or dev.instance_id,
                instance_id=dev.instance_id, installed_version=dev.driver_version,
                available_version="Check Catalog" if not dev.driver_version else "Update available",
                source="ms-catalog", download_url=catalog_search_url(hwid),
                arch=arch,
                title=f"{dev.friendly_name or dev.dev_class} — search Microsoft Update Catalog"
                + (f" ({hint['vendor']})" if hint["vendor"] else "")))
    return updates
