"""Game Boost: list top-memory processes, end user-selected ones.

Conservative by design: critical system processes are never listed, every kill
needs an explicit click. No services touched, no registry tweaks.
"""
from __future__ import annotations
import csv
import io
import subprocess

BLOCKED = {"system", "registry", "smss.exe", "csrss.exe", "wininit.exe",
           "services.exe", "lsass.exe", "lsaiso.exe", "svchost.exe",
           "winlogon.exe", "dwm.exe", "explorer.exe", "taskhostw.exe",
           "sihost.exe", "ctfmon.exe", "audiodg.exe", "conhost.exe",
           "idle", "system interrupts"}


def top_processes(limit: int = 25, timeout: int = 60) -> list[dict]:
    """Heaviest processes by working set (name, pid, MB)."""
    try:
        p = subprocess.run(["tasklist", "/FO", "CSV", "/NH"],
                           capture_output=True, text=True, timeout=timeout,
                           encoding="utf-8", errors="replace")
    except Exception:
        return []
    rows = []
    try:
        for r in csv.reader(io.StringIO(p.stdout or "")):
            if len(r) < 5:
                continue
            name = (r[0] or "").strip().strip('"')
            if not name or name.lower() in BLOCKED:
                continue
            try:
                pid = int((r[1] or "0").strip('" '))
                mem = int((r[4] or "0").replace(",", "").replace(" K", "").replace("K", "").strip('" '))
            except ValueError:
                continue
            rows.append({"name": name, "pid": pid, "mb": round(mem / 1024)})
    except Exception:
        return []
    rows.sort(key=lambda x: x["mb"], reverse=True)
    return rows[:limit]


def kill(pid: int, timeout: int = 60) -> dict:
    try:
        pid = int(pid)
    except (TypeError, ValueError):
        return {"ok": False, "log": "bad pid"}
    if pid < 100:
        return {"ok": False, "log": "refusing: system pid range"}
    try:
        p = subprocess.run(["taskkill", "/PID", str(pid), "/F"],
                           capture_output=True, text=True, timeout=timeout)
        ok = p.returncode == 0
        return {"ok": ok, "log": ((p.stdout or "") + (p.stderr or ""))[-500:]}
    except Exception as e:
        return {"ok": False, "log": str(e)}
