"""Backup via pnputil (WOW64-safe). Installs live in installer.py."""
from __future__ import annotations
import datetime
import os
import subprocess
from .arch import pnputil_path
from . import store as store_mod


def backup_all(dest_root: str, timeout: int = 1800) -> dict:
    """Export all third-party drivers into a fresh timestamped subfolder.

    A fresh subfolder is required because pnputil refuses non-empty targets.
    Can take many minutes and gigabytes; run elevated for a complete backup.
    """
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    dest = os.path.join(dest_root, f"full-{stamp}")
    try:
        os.makedirs(dest, exist_ok=False)
    except Exception as e:
        return {"ok": False, "id": -1, "path": dest, "log": f"cannot create {dest}: {e}"}
    cmd = [pnputil_path(), "/export-driver", "*", dest]
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        ok = p.returncode == 0
        bid = store_mod.record_backup("*", "full-backup", dest) if ok else -1
        return {"ok": ok, "id": bid, "path": dest, "log": (p.stdout + p.stderr)[-2000:]}
    except subprocess.TimeoutExpired:
        return {"ok": False, "id": -1, "path": dest,
                "log": f"export timed out after {timeout}s; partial data may remain"}
    except Exception as e:
        return {"ok": False, "id": -1, "path": dest, "log": str(e)}
