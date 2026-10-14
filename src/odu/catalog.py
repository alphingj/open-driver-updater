"""Driver source resolver. Open payloads only, fetched on demand, never bundled.

Implemented source: Microsoft Update Catalog (WHQL) via search + download dialog.
Planned: Windows Update Agent API, vendor-direct download pages, SDIO packs,
local INF folders (see README roadmap).
"""
from __future__ import annotations
import concurrent.futures
import html as html_mod
import json
import re
import urllib.parse
import urllib.request
from .arch import native_arch
from .models import Device, DriverUpdate
from . import store as store_mod

MS_CATALOG_SEARCH = "https://www.catalog.update.microsoft.com/Search.aspx?q="
MS_CATALOG_DIALOG = "https://www.catalog.update.microsoft.com/DownloadDialog.aspx"
_GUID = r"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}"
_MAX_HTML = 2 * 1024 * 1024

#: HWID prefixes with no downloadable Catalog drivers (virtual/inbox devices).
#: Still listed in Devices; never offered as updates.
SKIP_PREFIXES = ("SWD\\", "ROOT\\", "STORAGE\\", "MMDEVAPI\\", "PRINTENUM\\",
                 "BTH\\", "UEFI\\", "HTREE\\")

VENDOR_HINTS: list[tuple[str, str, str]] = [
    ("VEN_8086", "Intel", "https://www.intel.com/content/www/us/en/download-center.html"),
    ("VEN_10DE", "NVIDIA", "https://www.nvidia.com/drivers"),
    ("VEN_1002", "AMD", "https://www.amd.com/en/support/downloads.html"),
    ("VEN_1022", "AMD", "https://www.amd.com/en/support/downloads.html"),
    ("VEN_10EC", "Realtek", "https://www.realtek.com/Download/Index"),
    ("VEN_14E4", "Broadcom", "https://www.broadcom.com/support/download-search"),
    ("VEN_0E8D", "MediaTek", "https://www.mediatek.com/products"),
    ("VEN_13D3", "AzureWave", "https://www.azurewave.com/support.aspx"),
    ("VEN_17AA", "Lenovo", "https://pcsupport.lenovo.com/drivers"),
    ("VEN_1043", "ASUS", "https://www.asus.com/support/download-center/"),
    ("VEN_1028", "Dell", "https://www.dell.com/support/home/drivers"),
    ("VEN_103C", "HP", "https://support.hp.com/drivers"),
]


def _ven(hwid: str) -> str:
    m = re.search(r"(?:VEN|VID)_([0-9A-Fa-f]{4})", hwid or "")
    return ("VEN_" + m.group(1).upper()) if m else ""


def vendor_hint(hwid: str) -> dict:
    for ven, name, url in VENDOR_HINTS:
        if _ven(hwid) == ven:
            return {"vendor": name, "url": url}
    return {"vendor": "", "url": ""}


def catalog_search_url(hwid: str) -> str:
    return MS_CATALOG_SEARCH + urllib.parse.quote(hwid)


def search_url_for(hwid: str) -> str:
    """Static Catalog link most likely to show results (no HTTP cost).

    Full HWIDs with REV/SUBSYS suffixes score zero; the buried
    VEN_xxxx&DEV_xxxx (or VID/PID) pair is what the Catalog indexes.
    """
    cands = query_candidates(hwid)
    for q in cands:
        if re.fullmatch(r"(?:VEN|VID)_[0-9A-Fa-f]{4}&(?:DEV|PID)_[0-9A-Fa-f]{4}",
                        q, re.I):
            return catalog_search_url(q)
    return catalog_search_url(cands[-1] if cands else hwid)


def _fetch(url: str, timeout: int, data: bytes | None = None) -> str:
    headers = {"User-Agent": "Mozilla/5.0"}
    if data is not None:
        headers["Content-Type"] = "application/x-www-form-urlencoded"
    req = urllib.request.Request(url, data=data, headers=headers)
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read(_MAX_HTML).decode("utf-8", "ignore")


def query_candidates(hwid: str) -> list[str]:
    """Full HWID first, then progressively shorter forms.

    MS Catalog search is near-literal: 'USB\\VID_x&PID_y&REV_z' often scores
    zero while 'USB\\VID_x&PID_y' hits. Same for PCI SUBSYS/REV suffixes.
    """
    hwid = (hwid or "").strip()
    if not hwid:
        return []
    cands = [hwid]
    # PCI/ACPI style: the VEN_xxxx&DEV_xxxx pair buried mid-string is what the
    # Catalog indexes (e.g. ACP\...\VEN_1022&DEV_15E2\... -> VEN_1022&DEV_15E2).
    m = re.search(r"((?:VEN|VID)_[0-9A-Fa-f]{4}&(?:DEV|PID)_[0-9A-Fa-f]{4})",
                  hwid, re.I)
    if m and m.group(1) not in cands:
        cands.append(m.group(1))
    if "&" in hwid:
        head, rest = hwid.split("\\", 1) if "\\" in hwid else ("", hwid)
        parts = rest.split("&")
        for n in (2, 1):
            if len(parts) > n:
                short = (head + "\\" + "&".join(parts[:n])) if head else "&".join(parts[:n])
                if short not in cands:
                    cands.append(short)
    return cands


def _cached_query(query: str, timeout: int = 15) -> list[dict]:
    """Single Catalog query with 7-day result cache (keyed by query string)."""
    try:
        cached = store_mod.cache_get("q:" + query)
    except Exception:
        cached = None
    if cached:
        try:
            parsed = json.loads(cached)
            if isinstance(parsed, list):
                return parsed
        except Exception:
            pass
    try:
        found = _check_ms_catalog_query(query, timeout)
    except Exception:
        found = []
    try:
        store_mod.cache_put("q:" + query, json.dumps(found))
    except Exception:
        pass
    return found


def check_ms_catalog(hwid: str, timeout: int = 15) -> list[dict]:
    """Best-effort live lookup of Microsoft Update Catalog (HTML, may change)."""
    for q in query_candidates(hwid):
        if _cached_query(q, timeout):
            return _cached_query(q, timeout)
    return []


def best_search_url(hwid: str, timeout: int = 15) -> tuple[str, int]:
    """Catalog search URL most likely to show results + its hit count.

    Probes candidates (cache-backed) and returns the first query with hits;
    falls back to the full-HWID search when nothing matches.
    """
    for q in query_candidates(hwid):
        hits = _cached_query(q, timeout)
        if hits:
            return catalog_search_url(q), len(hits)
    return catalog_search_url(hwid), 0


def _check_ms_catalog_query(query: str, timeout: int = 15) -> list[dict]:
    html = _fetch(MS_CATALOG_SEARCH + urllib.parse.quote(query), timeout)
    # Catalog embeds results as: onclick='goToDetails("guid");' + <a id='guid_link'>Title</a>
    guids = re.findall(r"goToDetails\(&quot;(" + _GUID + r")&quot;\)", html, re.I)
    if not guids:
        guids = re.findall(r"goToDetails\(\"(" + _GUID + r")\"\)", html, re.I)
    titles: dict[str, str] = {}
    for m in re.finditer(r"(" + _GUID + r")_link'[^>]*>\s*([^<]{1,200})", html, re.I):
        titles[m.group(1).lower()] = html_mod.unescape(re.sub(r"\s+", " ", m.group(2)).strip())
    sizes: dict[str, int] = {}
    for m in re.finditer(r"(" + _GUID + r")_originalSize\"?>(\d+)<", html, re.I):
        try:
            sizes[m.group(1).lower()] = int(m.group(2))
        except ValueError:
            pass
    out = []
    for guid in guids[:10]:
        title = titles.get(guid.lower(), "")
        if not title:
            continue
        out.append({
            "id": guid,
            "title": title,
            "size": sizes.get(guid.lower(), 0),
            "url": f"https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid={guid}",
        })
    return out


def get_download_urls(update_id: str, size: int = 0, timeout: int = 30) -> list[dict]:
    """Resolve a Catalog update GUID to direct CAB URLs (POSTs the basket form).

    Returns [{url, sha256}]. Empty list when the dialog has no files.
    """
    update_id = (update_id or "").strip()
    if not re.fullmatch(_GUID, update_id):
        return []
    payload = json.dumps([{"size": size, "languages": "",
                           "uidInfo": update_id, "updateID": update_id}])
    data = urllib.parse.urlencode({
        "updateIDs": payload, "updateIDsBlockedForImport": "", "contentImport": "",
        "sku": "", "serverName": "", "ssl": "", "portNumber": "",
        "version": "", "protocol": "",
    }).encode()
    try:
        html = _fetch(MS_CATALOG_DIALOG, timeout, data)
    except Exception:
        return []
    urls = [(m.group(1), m.group(2)) for m in
            re.finditer(r"files\[(\d+)\]\.url\s*=\s*'(https://[A-Za-z0-9.\-]*"
                        r"download\.windowsupdate\.com/[^']+)'", html, re.I)]
    sha = {m.group(1): m.group(2) for m in
           re.finditer(r"files\[(\d+)\]\.sha256\s*=\s*'([A-Za-z0-9+/=]+)'", html)}
    out, seen = [], set()
    for idx, u in urls:
        if u in seen:
            continue
        seen.add(u)
        out.append({"url": u, "sha256": sha.get(idx, "")})
    return out


def _is_skipped(hwid: str) -> bool:
    return hwid.upper().startswith(SKIP_PREFIXES)


def _needs(dev: Device) -> bool:
    if dev.status == "DISCONNECTED":
        return False
    return dev.problem_code != 0 or dev.status == "ERROR" or not dev.driver_version


def resolve_updates(devices: list[Device], arch: str | None = None,
                    live_catalog: bool = False) -> list[DriverUpdate]:
    arch = native_arch() if arch is None else arch
    excluded = set(store_mod.get_exclusions())
    cands = [d for d in devices
             if d.hwid and not _is_skipped(d.hwid[0])
             and d.hwid[0] not in excluded and d.instance_id not in excluded
             and _needs(d)]
    live: dict[str, list[dict]] = {}
    if live_catalog and cands:
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            futs = {pool.submit(check_ms_catalog, d.hwid[0]): d.instance_id for d in cands}
            for fut in concurrent.futures.as_completed(futs):
                try:
                    live[futs[fut]] = fut.result() or []
                except Exception:
                    live[futs[fut]] = []
    updates: list[DriverUpdate] = []
    for dev in cands:
        hwid = dev.hwid[0]
        hint = vendor_hint(hwid)
        found = live.get(dev.instance_id, []) if live_catalog else []
        if found:
            f = found[0]
            updates.append(DriverUpdate(
                hwid=hwid, device_name=dev.friendly_name or dev.instance_id,
                instance_id=dev.instance_id, installed_version=dev.driver_version,
                available_version=f["title"][:64], source="ms-catalog",
                download_url=f["url"], arch=arch, title=f["title"],
                update_id=f["id"], vendor=hint["vendor"], vendor_url=hint["url"]))
            continue
        updates.append(DriverUpdate(
            hwid=hwid, device_name=dev.friendly_name or dev.instance_id,
            instance_id=dev.instance_id, installed_version=dev.driver_version,
            available_version="Check Catalog" if not dev.driver_version else "Update available",
            source="ms-catalog", download_url=search_url_for(hwid),
            arch=arch,
            title=f"{dev.friendly_name or dev.dev_class} — search Microsoft Update Catalog"
            + (f" ({hint['vendor']})" if hint["vendor"] else ""),
            vendor=hint["vendor"], vendor_url=hint["url"]))
    return updates
