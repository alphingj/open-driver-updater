"""Selective per-driver backup: enumerate published oemNN.inf drivers and
export chosen ones (pnputil /export-driver <oem> <dir>)."""
from __future__ import annotations
import os
import re
import subprocess
import time
from .arch import pnputil_path
from . import store as store_mod


def enum_drivers(timeout: int = 120) -> list[dict]:
    """Published third-party drivers from pnputil /enum-drivers."""
    try:
        p = subprocess.run([pnputil_path(), "/enum-drivers"],
                           capture_output=True, text=True, timeout=timeout,
                           encoding="utf-8", errors="replace")
    except Exception:
        return []
    out, cur = [], {}
    for line in (p.stdout or "").splitlines():
        line = line.strip()
        if not line:
            if cur.get("oem"):
                out.append(cur)
            cur = {}
            continue
        m = re.match(r"Published Name\s*:\s*(oem\d+\.inf)", line, re.I)
        if m:
            cur["oem"] = m.group(1).lower()
            continue
        for key, field in (("Original Name", "original"), ("Provider Name", "provider"),
                           ("Class Name", "class"), ("Driver Version", "version"),
                           ("Signer Name", "signer")):
            m = re.match(re.escape(key) + r"\s*:\s*(.+)", line)
            if m:
                cur[field] = m.group(1).strip()
    if cur.get("oem"):
        out.append(cur)
    return out


def export_driver(oem: str, dest_root: str) -> dict:
    """Export one published driver package to a timestamped folder."""
    if not re.fullmatch(r"oem\d+\.inf", (oem or "").lower()):
        return {"ok": False, "id": -1, "path": "", "log": "refusing: not an oem name"}
    dest = os.path.join(dest_root, oem.lower() + "-" + time.strftime("%Y%m%d-%H%M%S"))
    try:
        os.makedirs(dest, exist_ok=False)
    except Exception as e:
        return {"ok": False, "id": -1, "path": dest, "log": str(e)}
    try:
        p = subprocess.run([pnputil_path(), "/export-driver", oem.lower(), dest],
                           capture_output=True, text=True, timeout=300)
        ok = p.returncode == 0
        bid = store_mod.record_backup(oem.lower(), "single-backup", dest) if ok else -1
        return {"ok": ok, "id": bid, "path": dest, "log": (p.stdout + p.stderr)[-2000:]}
    except Exception as e:
        return {"ok": False, "id": -1, "path": dest, "log": str(e)}
