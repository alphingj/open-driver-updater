"""Weekly scan scheduler via schtasks.exe (x86/x64/ARM64, no extra deps).

Scan itself runs unelevated, so the task requests no elevation (/RL).
"""
from __future__ import annotations
import os
import subprocess
import sys

TASK_NAME = "OpenDriverUpdaterScan"


def _run(cmd: list[str], timeout: int = 60) -> tuple[int, str]:
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return p.returncode, (p.stdout + p.stderr)[-2000:]
    except Exception as e:
        return 1, str(e)


def _scan_command() -> str:
    """Command line for the scheduled scan.

    Source checkouts need PYTHONPATH=src for `-m odu.cli` to resolve, so wrap
    in powershell; pip-installed environments run the module directly.
    """
    exe = sys.executable
    try:
        import odu
        src_run = "site-packages" not in (odu.__file__ or "").replace("/", os.sep)
    except Exception:
        src_run = True
    if not src_run:
        return f'"{exe}" -m odu.cli scan'
    src = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src")
    return (f'powershell.exe -NoProfile -Command "& {{ $env:PYTHONPATH=\'{src}\';'
            f' & \'{exe}\' -m odu.cli scan }}"')


def enable_weekly() -> dict:
    cmd = ["schtasks", "/Create", "/TN", TASK_NAME, "/TR", _scan_command(),
           "/SC", "WEEKLY", "/D", "SUN", "/ST", "10:00", "/F"]
    code, log = _run(cmd)
    return {"ok": code == 0, "log": log}


def disable() -> dict:
    code, log = _run(["schtasks", "/Delete", "/TN", TASK_NAME, "/F"])
    return {"ok": code == 0, "log": log}


def status() -> dict:
    code, log = _run(["schtasks", "/Query", "/TN", TASK_NAME])
    return {"enabled": code == 0, "log": log}
