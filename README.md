# Open Driver Updater — 100% free (MIT)

Free clone of the paid Advanced Driver Updater workflow: **Scan → Review → Back up → Update**.
No paywall, no proprietary cloud DB. Drivers are open payloads fetched on demand from
Windows Update / Microsoft Update Catalog / vendor pages / SDIO packs.

## Compatibility

`x86 (32-bit) + x64 + ARM64` on Windows 10/11 (7/8.1 best-effort on x86/x64).
Core is stdlib-only Python, so the same code runs on every arch. INF arch tags
(`NTX86`/`NTAMD64`/`NTARM64`) are filtered to the running machine.

## Run (no install)

```powershell
python -m odu.cli scan
python -m odu.cli serve --port 8901   # Apple-style web UI
```

Or from repo root with `src` layout:

```powershell
$env:PYTHONPATH="src"; python -m odu.cli serve
```

Open http://127.0.0.1:8901/

## Sources (open)

1. Local PnP + `pnputil` (offline)
2. Microsoft Update Catalog (WHQL) — search link per HWID, optional live lookup
3. Vendor-direct (Intel/NVIDIA/AMD/Realtek hints by `VEN_xxxx`)
4. SDIO open packs (fallback)

Binaries are never bundled; the app downloads at install time after showing the vendor EULA page.

## Build per-arch exe

```powershell
pip install pyinstaller
pyinstaller --onefile --name odu-x64 -p src -m odu.cli
```

Build natively on each arch (x86 machine → `odu-x86.exe`, ARM64 → `odu-arm64.exe`).
`pyinstaller` supports all three; do not cross-build.

## Tests

```powershell
pip install pytest; pytest -q
```

## Legal (clean-room)

Reimplemented from observed behavior + public docs. No Systweak code, strings,
icons, or driver DB copied. Installer `adusetup*.exe` (v4.6.1086.11084) used only for
behavioral reference via REA static inspection.
