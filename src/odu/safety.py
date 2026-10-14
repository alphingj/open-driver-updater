"""System safety: restore points and reboot-pending detection (stdlib only)."""
from __future__ import annotations
import subprocess
import sys


def _win() -> bool:
    return sys.platform == "win32"


def create_restore_point(description: str = "Open Driver Updater",
                         timeout: int = 300) -> dict:
    """WMI SystemRestore checkpoint. Needs elevation; safe no-op otherwise."""
    if not _win():
        return {"ok": False, "log": "not Windows"}
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
        return {"ok": False, "log": out[-500:] or p.stderr[-500:]}
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
                    winreg.QueryValueEx(k, value)
                    reasons.append(value)
                    break
            except OSError:
                continue
    return {"pending": bool(reasons), "reasons": reasons}
