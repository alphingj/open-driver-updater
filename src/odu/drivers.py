"""Selective per-driver backup: enumerate published oemNN.inf drivers and
export chosen ones (pnputil /export-driver <oem> <dir>)."""
from __future__ import annotations
import datetime
import os
import re
import subprocess
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


def _export_dir(dest_root: str, oem: str) -> str:
    """Unique target folder, since pnputil refuses a non-empty destination."""
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    dest = os.path.join(dest_root, oem.lower() + "-" + stamp)
    n = 2
    while os.path.exists(dest):
        dest = os.path.join(dest_root, oem.lower() + "-" + stamp + "-" + str(n))
        n += 1
    return dest


def export_driver(oem: str, dest_root: str) -> dict:
    """Export one published driver package to a timestamped folder."""
    if not re.fullmatch(r"oem\d+\.inf", (oem or "").lower()):
        return {"ok": False, "id": -1, "path": "", "log": "refusing: not an oem name"}
    dest = _export_dir(dest_root, oem)
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


def parse_current_inf(text: str) -> str:
    """First oemNN.inf token in ``pnputil /enum-devices /drivers`` output.

    The active "Driver Name" is printed before extension/matching drivers, so
    the first match is the package actually bound to the device. Localization
    only changes the label, never the oem file name.
    """
    m = re.search(r"\b(oem\d+\.inf)\b", text or "", re.I)
    return m.group(1).lower() if m else ""


def current_inf(instance_id: str, timeout: int = 60) -> str:
    """Published oemNN.inf currently bound to a device ("" if none/in-box)."""
    if not instance_id:
        return ""
    try:
        p = subprocess.run(
            [pnputil_path(), "/enum-devices", "/instanceid", instance_id, "/drivers"],
            capture_output=True, text=True, timeout=timeout,
            encoding="utf-8", errors="replace")
    except Exception:
        return ""
    if p.returncode != 0:
        return ""
    return parse_current_inf(p.stdout or "")


def capture_previous(instance_id: str, dest_root: str) -> dict:
    """Export the driver bound to a device *before* upgrading it.

    Returns ``{"ok", "oem", "path", "log"}``. ``oem`` is empty when the device
    runs an in-box driver — nothing to restore, and rollback falls back to
    Windows' default driver instead.
    """
    oem = current_inf(instance_id)
    if not oem:
        return {"ok": True, "oem": "", "path": "",
                "log": "no third-party driver bound; nothing to export"}
    exp = export_driver(oem, dest_root)
    return {"ok": bool(exp.get("ok")), "oem": oem,
            "path": exp.get("path", ""), "log": exp.get("log", "")}
