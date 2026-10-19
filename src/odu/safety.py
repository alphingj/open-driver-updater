"""System safety: restore points and reboot-pending detection (stdlib only)."""
from __future__ import annotations
import subprocess
import sys


def _win() -> bool:
    return sys.platform == "win32"


def _recent_restore_point(within_hours: int = 24) -> bool:
    """True when a restore point was created recently (avoids the 24h
    Checkpoint-Computer frequency failure looking like a real error)."""
    if not _win():
        return False
    ps = ("Get-ComputerRestorePoint | Sort-Object CreationTime -Descending | "
          "Select-Object -First 1 | "
          "ForEach-Object { $_.CreationTime.ToString('o') }")
    try:
        p = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                           capture_output=True, text=True, timeout=120)
        import datetime
        ts = (p.stdout or "").strip()
        if not ts:
            return False
        try:
            when = datetime.datetime.fromisoformat(ts)
        except ValueError:
            return False
        if when.tzinfo is not None:
            when = when.replace(tzinfo=None)
        return (datetime.datetime.now() - when).total_seconds() < within_hours * 3600
    except Exception:
        return False


def create_restore_point(description: str = "Open Driver Updater",
                         timeout: int = 300) -> dict:
    """WMI SystemRestore checkpoint. Needs elevation; safe no-op otherwise."""
    if not _win():
        return {"ok": False, "log": "not Windows"}
    if _recent_restore_point():
        return {"ok": True, "log": "recent restore point exists, skipped"}
    safe = description.replace(chr(39), "")
    ps = ("Try { Checkpoint-Computer -Description '" + safe + "' "
          "-RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop; 'OK' } "
          "Catch { 'FAIL: ' + $_.Exception.Message }")
    try:
        p = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                           capture_output=True, text=True, timeout=timeout)
        out = (p.stdout or "").strip()
        if out == "OK":
            return {"ok": True, "log": "restore point created"}
        if "1440" in out or "already been created" in out:
            # Benign: Windows caps manual points at one per 24h, so a recent
            # point already exists. Treat as safety-net-present, say so.
            return {"ok": True,
                    "log": "recent restore point already exists (Windows allows "
                           "one per 24 hours) — reused"}
        low = out.lower()
        if ("system restore is disabled" in low
                or "not enabled on the system drive" in low
                or "system protection is disabled" in low
                or "0x80070422" in low):
            # System Protection is off (common on managed/SSD-tuned PCs).
            # Blocking every update would be worse than proceeding: the full
            # per-driver backup still runs and rollback is per-package.
            return {"ok": True,
                    "log": "System Protection is disabled on this PC — restore "
                           "point skipped; the per-driver backup still runs"}
        return {"ok": False, "log": out[-500:] or (p.stderr or "")[-500:]}
    except Exception as e:
        return {"ok": False, "log": str(e)}


_REBOOT_KEYS = [
    (r"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations"),
    (r"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing", "RebootPending"),
    (r"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update", "RebootRequired"),
]


def reboot_pending() -> dict:
    """True when Windows wants a restart (file moves, CBS, or WU flags)."""
    if not _win():
        return {"pending": False, "reasons": []}
    try:
        import winreg
    except ImportError:
        return {"pending": False, "reasons": []}
    reasons = []
    for key_path, value in _REBOOT_KEYS:
        for hive in (0x80000002,):  # HKLM
            try:
                with winreg.OpenKey(hive, key_path) as k:
                    data, _ = winreg.QueryValueEx(k, value)
                    # Present-but-empty values (common for file-rename ops)
                    # mean nothing is actually pending.
                    if data:
                        reasons.append(value)
                    break
            except OSError:
                continue
    return {"pending": bool(reasons), "reasons": reasons}
