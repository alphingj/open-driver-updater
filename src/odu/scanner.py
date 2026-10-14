"""Device scanner: WMI/CIM + pnputil, stdlib-only, mockable for tests."""
from __future__ import annotations
import json
import subprocess
from .models import Device


def _run(cmd: list[str], timeout: int = 60) -> str:
    try:
        out = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return out.stdout or ""
    except Exception:
        return ""


def scan_powershell() -> list[Device]:
    ps = (
        "Get-PnpDevice | Select-Object InstanceId,FriendlyName,Class,"
        "Manufacturer,Status,Problem | ConvertTo-Json -Depth 3 -Compress"
    )
    raw = _run(["powershell", "-NoProfile", "-Command", ps])
    if not raw.strip():
        return []
    try:
        data = json.loads(raw)
    except Exception:
        return []
    if isinstance(data, dict):
        data = [data]
    devs: list[Device] = []
    for d in data or []:
        problem = int(d.get("Problem") or 0)
        status = str(d.get("Status") or "OK")
        devs.append(Device(
            instance_id=str(d.get("InstanceId") or ""),
            friendly_name=str(d.get("FriendlyName") or ""),
            dev_class=str(d.get("Class") or ""),
            manufacturer=str(d.get("Manufacturer") or ""),
            status="ERROR" if problem not in (0,) or status.upper() == "ERROR" else "OK",
            problem_code=problem,
        ))
    # Enrich HWIDs + driver version via Get-PnpDeviceProperty (batched, best-effort)
    hw = _run(["powershell", "-NoProfile", "-Command",
               "Get-PnpDevice | Get-PnpDeviceProperty -KeyName DEVPKEY_Device_HardwareIds | "
               "Select-Object InstanceId,Data | ConvertTo-Json -Depth 3 -Compress"])
    try:
        hdata = json.loads(hw) if hw.strip() else []
        if isinstance(hdata, dict):
            hdata = [hdata]
        hmap = {str(x.get("InstanceId")): list(x.get("Data") or []) for x in hdata}
        for dev in devs:
            dev.hwid = [str(v) for v in hmap.get(dev.instance_id, [])][:8]
    except Exception:
        pass
    drv = _run(["powershell", "-NoProfile", "-Command",
                "Get-WmiObject Win32_PnPSignedDriver | Select-Object DeviceID,DriverVersion,"
                "DriverDate,DriverProviderName | ConvertTo-Json -Depth 2 -Compress"])
    try:
        ddata = json.loads(drv) if drv.strip() else []
        if isinstance(ddata, dict):
            ddata = [ddata]
        dmap = {str(x.get("DeviceID")): x for x in ddata}
        for dev in devs:
            m = dmap.get(dev.instance_id)
            if m:
                dev.driver_version = str(m.get("DriverVersion") or "")
                dev.driver_provider = str(m.get("DriverProviderName") or "")
                dev.driver_date = str(m.get("DriverDate") or "")[:8]
    except Exception:
        pass
    return [d for d in devs if d.instance_id]


def scan(mock: list[Device] | None = None) -> list[Device]:
    if mock is not None:
        return mock
    devs = scan_powershell()
    if devs:
        return devs
    # Non-Windows / CI fallback: empty list (UI shows demo row separately)
    return []
