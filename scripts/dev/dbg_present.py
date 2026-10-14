import subprocess

IDS = [
    "USBSTOR\\CDROM&VEN_GENERIC&PROD_USB_CDDVD_DRIVE&REV_1.00\\202301010001&0",
    "BTH\\MS_BTHPAN\\9&F29EB49&0&2",
    "ACPI\\PNP0C0C\\AA",
    "PCI\\VEN_1022&DEV_15E3&SUBSYS_16031043&REV_00\\4&317E80FF&0&0641",
]
ps = ("$ids = @(" + ";".join(f"'{i}'" for i in IDS) + ");"
      " foreach ($x in $ids) { $d = Get-PnpDevice -InstanceId $x -ErrorAction SilentlyContinue;"
      " if ($d) { $p = $d | Get-PnpDeviceProperty -KeyName DEVPKEY_Device_IsPresent,DEVPKEY_Device_ProblemCode -ErrorAction SilentlyContinue;"
      " Write-Output ($x + ' Problem=' + $d.Problem + ' Status=' + $d.Status + ' Props=' + (($p | ForEach-Object { $_.KeyName + '=' + $_.Data }) -join ',')) } }")
out = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                     capture_output=True, timeout=120,
                     encoding="utf-8", errors="replace")
print(out.stdout or out.stderr)
