"""Backup / restore via pnputil (WOW64-safe), DISM fallback note."""
from __future__ import annotations
import os
import subprocess
import time
from .arch import pnputil_path
from . import store as store_mod


def backup_all(dest: str) -> dict:
    os.makedirs(dest, exist_ok=True)
    cmd = [pnputil_path(), "/export-driver", "*", dest]
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
        ok = p.returncode == 0
        bid = store_mod.record_backup("*", "full-backup", dest) if ok else -1
        return {"ok": ok, "id": bid, "path": dest, "log": (p.stdout + p.stderr)[-2000:]}
    except Exception as e:
        return {"ok": False, "id": -1, "path": dest, "log": str(e)}


def backup_device(instance_id: str, dest_root: str) -> dict:
    dest = os.path.join(dest_root, f"backup-{int(time.time())}")
    return backup_all(dest)


def install_driver(inf_or_dir: str) -> dict:
    cmd = [pnputil_path(), "/add-driver", inf_or_dir, "/install"]
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
        return {"ok": p.returncode == 0, "log": (p.stdout + p.stderr)[-4000:]}
    except Exception as e:
        return {"ok": False, "log": str(e)}
