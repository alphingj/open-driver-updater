# Open Driver Updater — 100% free (MIT)

Free clone of the paid Advanced Driver Updater workflow: **Scan → Review → Back up → Update**.
No paywall, no proprietary cloud DB. Drivers are open payloads fetched on demand from
Microsoft Update Catalog (WHQL) with vendor pages as hints.

## Compatibility

`x86 (32-bit) + x64 + ARM64` on Windows 10/11 (7/8.1 best-effort on x86/x64).
Core is stdlib-only Python, so the same code runs on every arch. Driver matching
always uses the **OS** architecture (`native_arch`, WOW64-safe), and INF arch tags
(`NTX86`/`NTAMD64`/`NTARM64`) are matched exactly — no wrong-arch installs.

## Run (no install)

```powershell
$env:PYTHONPATH="src"; python -m odu.cli scan
$env:PYTHONPATH="src"; python -m odu.cli serve --port 8901   # Apple-style web UI
$env:PYTHONPATH="src"; python -m odu.cli backup              # pnputil export (elevated)
$env:PYTHONPATH="src"; python -m odu.cli update-all          # backup + install all (elevated)
```

Open http://127.0.0.1:8901/ — press **Scan now**, tick **Live Catalog**, then **Update all**.
Updates install sequentially; a failed pre-update backup stops the queue before
anything is touched. A restart may be required afterwards.

## How updates resolve

1. Local PnP scan (problem devices, missing drivers). Disconnected ghosts
   (`problem 45`) and virtual devices (`SWD\*`, `ROOT\*`, …) are shown but never
   offered as updates.
2. Hardware-ID lookup against Microsoft Update Catalog with progressive fallback
   (`VID&PID&REV` → `VID&PID`), cached 7 days, 4-way parallel.
3. One click downloads the Catalog CAB (SHA-256 verified), expands it, picks the
   arch-compatible INF, and installs via `pnputil` (signatures enforced by Windows).

## Trust & privacy

- Web UI binds `127.0.0.1` only; any local process can call it, installs additionally
  need elevation. No accounts, no telemetry.
- HWIDs are sent to Microsoft Update Catalog (required to find drivers), nothing else.
- Binaries are never bundled; drivers download at install time from Microsoft.

## Build per-arch exe

```powershell
pip install pyinstaller
pyinstaller --onefile --name odu-x64 -p src -m odu.cli
```

Build natively on each arch (x86 machine → `odu-x86.exe`, ARM64 → `odu-arm64.exe`).
Do not cross-build.

## Tests

```powershell
python -m unittest discover -s tests -v   # stdlib only, no pytest needed
```

`scripts/` holds repeatable verifiers (`verify_*.py`, `test_*.py`); `scripts/dev/`
holds one-off Catalog probes.

## Roadmap

Windows Update Agent API, vendor-direct CAB downloads, SDIO packs, local INF
folders, per-device rollback (beyond full export), reboot detection.

## Legal (clean-room)

Reimplemented from observed behavior + public docs. No Systweak code, strings,
icons, or driver DB copied. Installer `adusetup*.exe` (v4.6.1086.11084) used only for
behavioral reference via REA static inspection.
