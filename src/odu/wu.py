"""Windows Update driver source via the built-in COM API (powershell host).

Lists driver updates WU offers for this machine (title + KB), and can install
a chosen set. Elevation required for install; search works as user.
"""
from __future__ import annotations
import json
import subprocess

_PS_SEARCH = r"""
$ s = New-Object -ComObject Microsoft.Update.Session
$sr = $s.CreateUpdateSearcher()
$r = $sr.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'")
$out = @()
foreach ($u in $r.Updates) {
  $kbs = @()
  foreach ($kb in $u.KBArticleIDs) { $kbs += $kb }
  $out += [pscustomobject]@{ Title = $u.Title; KB = ($kbs -join ',');
    Reboot = $u.RebootRequired; Size = $u.MaxDownloadSize }
}
$out | ConvertTo-Json -Compress
"""

def _run(ps: str, timeout: int) -> str:
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                             capture_output=True, text=True, timeout=timeout,
                             encoding="utf-8", errors="replace")
        return out.stdout or ""
    except Exception:
        return ""


def search(timeout: int = 300) -> list[dict]:
    raw = _run(_PS_SEARCH, timeout)
    if not raw.strip():
        return []
    try:
        data = json.loads(raw)
    except Exception:
        return []
    if isinstance(data, dict):
        data = [data]
    out = []
    for d in data if isinstance(data, list) else []:
        if isinstance(d, dict) and d.get("Title"):
            out.append({"title": str(d["Title"]), "kb": str(d.get("KB") or ""),
                        "reboot": bool(d.get("Reboot")),
                        "size": int(d.get("Size") or 0)})
    return out


def install(titles: list[str], timeout: int = 1800) -> dict:
    if not titles:
        return {"ok": False, "log": "nothing selected"}
    want = ",".join("'" + t.replace("'", "''") + "'" for t in titles)
    ps = ("$Titles = @(" + want + ");\n"
          "$s = New-Object -ComObject Microsoft.Update.Session\n"
          "$sr = $s.CreateUpdateSearcher()\n"
          "$r = $sr.Search(\"IsInstalled=0 and IsHidden=0 and Type='Driver'\")\n"
          "$col = New-Object -ComObject Microsoft.Update.UpdateColl\n"
          "foreach ($u in $r.Updates) { if ($Titles -contains $u.Title) { $col.Add($u) | Out-Null } }\n"
          "if ($col.Count -eq 0) { 'NONE'; exit }\n"
          "$dl = $s.CreateUpdateDownloader(); $dl.Updates = $col; $dr = $dl.Download()\n"
          "if ($dr.ResultCode -ne 2) { 'DOWNLOAD:' + $dr.ResultCode; exit }\n"
          "$ins = $s.CreateUpdateInstaller(); $ins.Updates = $col; $ir = $ins.Install()\n"
          "'INSTALL:' + $ir.ResultCode + ' reboot=' + $ir.RebootRequired\n")
    raw = _run(ps, timeout)
    ok = raw.strip().startswith("INSTALL:2")
    return {"ok": ok, "log": raw.strip()[-1000:]}
