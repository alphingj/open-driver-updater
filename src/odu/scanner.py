"""Device scanner: PnP + CIM, stdlib-only, mockable for tests.

One PowerShell process returns three JSON blobs (devices, HWIDs, signed-driver
rows) separated by sentinel lines, keeping spawn overhead to a single process.
"""
from __future__ import annotations
import json
import subprocess
from .models import Device

#: Problem code 45 = device disconnected (ghost). Shown, never offered updates.
GHOST_PROBLEM = 45

_PS_SCRIPT = r"""
$devs = Get-PnpDevice | Select-Object InstanceId,FriendlyName,Class,Manufacturer,Status,Problem
$devs | ConvertTo-Json -Depth 3 -Compress
'__ODU_HW__'
Get-PnpDevice | Get-PnpDeviceProperty -KeyName DEVPKEY_Device_HardwareIds | Select-Object InstanceId,Data | ConvertTo-Json -Depth 3 -Compress
'__ODU_DRV__'
Get-CimInstance Win32_PnPSignedDriver | Select-Object DeviceID,DriverVersion,DriverDate,DriverProviderName | ConvertTo-Json -Depth 2 -Compress
"""


def _run_ps(timeout: int = 120) -> str:
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", _PS_SCRIPT],
                             capture_output=True, timeout=timeout,
                             encoding="utf-8", errors="replace")
        if out.returncode != 0:
            return ""
        return out.stdout or ""
    except Exception:
        return ""


def _as_list(raw: str) -> list:
    try:
        data = json.loads(raw) if raw.strip() else []
    except Exception:
        return []
    if isinstance(data, dict):
        return [data]
    return data if isinstance(data, list) else []


def _classify(problem: int, status: str) -> str:
    if problem == GHOST_PROBLEM:
        return "DISCONNECTED"
    if problem != 0 or status.upper() == "ERROR":
        return "ERROR"
    return "OK"


def scan_native(exe: str | None = None, timeout: int = 60) -> list[Device]:
    """Scan via the C++ odu_scan.exe (instant, no PowerShell). Empty on failure."""
    import os
    if exe is None:
        exe = find_scanner()
    if not exe:
        return []
    try:
        out = subprocess.run([exe], capture_output=True, timeout=timeout)
        if out.returncode != 0 or not out.stdout:
            return []
        data = json.loads(out.stdout.decode("utf-8", "ignore"))
    except Exception:
        return []
    devs: list[Device] = []
    for d in data if isinstance(data, list) else []:
        if not isinstance(d, dict) or not d.get("instance_id"):
            continue
        try:
            problem = int(d.get("problem_code") or 0)
        except (TypeError, ValueError):
            problem = 0
        devs.append(Device(
            instance_id=str(d.get("instance_id") or ""),
            friendly_name=str(d.get("friendly_name") or ""),
            dev_class=str(d.get("dev_class") or ""),
            manufacturer=str(d.get("manufacturer") or ""),
            hwid=[str(v) for v in (d.get("hwid") or [])][:8],
            status=str(d.get("status") or "OK"),
            problem_code=problem,
            driver_version=str(d.get("driver_version") or ""),
            driver_date=str(d.get("driver_date") or ""),
            driver_provider=str(d.get("driver_provider") or ""),
        ))
    return devs


def find_scanner() -> str:
    """Locate odu_scan.exe from a source checkout, OS-arch first.

    x64 runs emulated on ARM64 Windows; x86-only systems need the x86 build.
    """
    import os
    from .arch import native_arch
    order = ("x86", "x64") if native_arch() == "x86" else ("x64", "x86")
    here = os.path.abspath(__file__)
    for _ in range(5):
        here = os.path.dirname(here)
        for sub in order:
            cand = os.path.join(here, "native", "bin", sub, "odu_scan.exe")
            if os.path.isfile(cand):
                return cand
    return ""


def scan_powershell() -> list[Device]:
    raw = _run_ps()
    if not raw.strip():
        return []
    try:
        parts = raw.split("__ODU_HW__", 1)
        dev_json = parts[0]
        hw_json, drv_json = parts[1].split("__ODU_DRV__", 1)
    except (IndexError, ValueError):
        return []
    devs: list[Device] = []
    for d in _as_list(dev_json):
        if not isinstance(d, dict):
            continue
        try:
            problem = int(d.get("Problem") or 0)
        except (TypeError, ValueError):
            problem = 0
        status = str(d.get("Status") or "OK")
        devs.append(Device(
            instance_id=str(d.get("InstanceId") or ""),
            friendly_name=str(d.get("FriendlyName") or ""),
            dev_class=str(d.get("Class") or ""),
            manufacturer=str(d.get("Manufacturer") or ""),
            status=_classify(problem, status),
            problem_code=problem,
        ))
    hmap: dict[str, list] = {}
    for x in _as_list(hw_json):
        if isinstance(x, dict):
            hmap[str(x.get("InstanceId"))] = list(x.get("Data") or [])
    for dev in devs:
        dev.hwid = [str(v) for v in hmap.get(dev.instance_id, [])][:8]
    dmap: dict[str, dict] = {}
    for x in _as_list(drv_json):
        if isinstance(x, dict):
            dmap[str(x.get("DeviceID"))] = x
    for dev in devs:
        m = dmap.get(dev.instance_id)
        if m:
            dev.driver_version = str(m.get("DriverVersion") or "")
            dev.driver_provider = str(m.get("DriverProviderName") or "")
            dev.driver_date = str(m.get("DriverDate") or "")[:8]
    return [d for d in devs if d.instance_id]


def scan(mock: list[Device] | None = None) -> list[Device]:
    if mock is not None:
        return mock
    try:
        devs = scan_native()
        if devs:
            return devs
    except Exception:
        pass
    try:
        return scan_powershell()
    except Exception:
        return []
