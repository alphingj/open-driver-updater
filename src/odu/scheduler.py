"""Scheduler via schtasks.exe (works x86/x64/ARM64, no extra deps)."""
from __future__ import annotations
import subprocess
import sys

TASK_NAME = "OpenDriverUpdaterScan"


def _run(cmd: list[str], timeout: int = 60) -> tuple[int, str]:
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return p.returncode, (p.stdout + p.stderr)[-2000:]
    except Exception as e:
        return 1, str(e)


def enable_weekly() -> dict:
    exe = sys.executable
    cmd = ["schtasks", "/Create", "/TN", TASK_NAME, "/TR",
           f'"{exe} -m odu.cli scan"',
           "/SC", "WEEKLY", "/D", "SUN", "/ST", "10:00", "/F"]
    code, log = _run(cmd)
    return {"ok": code == 0, "log": log}


def disable() -> dict:
    code, log = _run(["schtasks", "/Delete", "/TN", TASK_NAME, "/F"])
    return {"ok": code == 0, "log": log}


def status() -> dict:
    code, log = _run(["schtasks", "/Query", "/TN", TASK_NAME])
    return {"enabled": code == 0, "log": log}
