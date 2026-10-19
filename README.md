# Open Driver Updater — 100% free (MIT)

Free driver updater for Windows: **Scan → Review → Back up → Update**.
No paywall, no proprietary cloud DB. Drivers are open payloads fetched on demand from
Microsoft Update Catalog (WHQL) with vendor pages as hints.

## Architecture (4 languages, each where it wins)

| Layer | Language | Path | Why |
|---|---|---|---|
| HWID utils (normalize, VEN/DEV, INF arch tags) | **C** | `native/odu_hw/` | dependency-free, shared semantics |
| Instant device scan (SetupAPI, 0 mismatches vs PnP) | **C++** | `native/odu_scan/` | 0.2s for 169 devices, no PowerShell |
| Desktop app (WPF, Apple-style UI) | **C#** | `cs/OduApp/` | native Windows UI, x86/x64/ARM64 |
| Resolver service, CLI, tests, automation | **Python** | `src/odu/` | web UI + API + CI |

C# and Python share the same native scanner (`native/bin/*/odu_scan.exe`).

## Compatibility

`x86 + x64 + ARM64` on Windows 10/11 (7/8.1 best-effort x86/x64).
Driver matching always uses the **OS** architecture (WOW64-safe), INF tags
(`NTX86`/`NTAMD64`/`NTARM64`) matched exactly — no wrong-arch installs.

## Install (recommended)

```powershell
installer\Output\OpenDriverUpdater-Setup-0.7.6.exe
```

Per-user install, no admin needed to install: Start Menu shortcut, Add/Remove
entry, both scanner archs (auto-selected at runtime). Run the app elevated when
backing up or installing drivers. Uninstaller removes the app and its scheduled
task; driver backups in `~\odu-backup` are kept.

## Build

```bat
native\build.bat   :: MSVC 2022: odu_hw.lib + odu_scan.exe (x64+x86) + C selftest
cs\build.bat       :: in-box Framework csc: OduApp.exe (no SDK/NuGet needed)
cs\test.bat        :: C# test runner, 20 asserts, exit code
"C:\Program Files\Inno Setup 7\ISCC.exe" installer\setup.iss
```

Requires VS2022 with MSVC + Windows SDK, and Inno Setup 7 for the installer.

## Run

```powershell
# Native desktop app (recommended)
cs\bin\OduApp.exe

# Headless self-test (C# + native scan, no GUI)
cs\bin\OduApp.exe --selftest

# Python service / CLI
$env:PYTHONPATH="src"; python -m odu.cli scan
$env:PYTHONPATH="src"; python -m odu.cli serve --port 8901   # web UI
$env:PYTHONPATH="src"; python -m odu.cli backup              # elevated
$env:PYTHONPATH="src"; python -m odu.cli update-all          # elevated
```

The Python scanner prefers `odu_scan.exe` automatically, falling back to
PowerShell/CIM when the binaries are absent.

## How updates resolve

1. Native scan (problem devices, missing drivers). Disconnected ghosts
   (`IsPresent=false`) and virtual devices (`SWD\*`, `ROOT\*`, …) are shown but
   never offered as updates.
2. Live Catalog lookup per Hardware ID with progressive fallback
   (`REV`/`SUBSYS` suffixes stripped, buried `VEN&DEV` extracted), cached 7 days.
   Hits pass a relevance gate (precise query, or title matches the device) and a
   version check: strictly newer → **Ready**, plausible but unverifiable →
   **Needs review** (manual install only, never auto-installed).
3. One click (per driver, selected, or all) downloads the Catalog CAB (SHA-256
   verified), expands it, picks the arch-compatible INF, and installs via
   `pnputil` (signatures enforced by Windows). Display/GPU updates ask twice.
4. Safety first: system restore point, then full driver backup — either failing
   stops the queue before anything is touched. Every install also exports the
   driver package currently bound to the device and records it with the new
   `oemNN.inf`, so one-click rollback both removes the new package and restores
   the previous version. Reboot need is detected and shown.

Also included: scheduled weekly scans, exclusion + skip-this-version lists,
Windows Update driver source, game-component (VC++/DirectX) checks, offline
folder/CAB install, HTML report export, and in-app update checks.

## Trust & privacy

- Web UI binds `127.0.0.1` only; installs need elevation. No accounts, no telemetry.
- HWIDs go to Microsoft Update Catalog (required to find drivers), nowhere else.
- Drivers download at install time from Microsoft; nothing is bundled or re-hosted.

## Tests

```powershell
python -m unittest discover -s tests -v   # stdlib only
native\build.bat                          # includes C selftest (HW SELFTEST OK)
cs\bin\OduApp.exe --selftest              # C# + native parity check
```

`scripts/` holds repeatable verifiers; `scripts/dev/` holds one-off probes.

## Roadmap

Vendor-direct feeds (Intel/NVIDIA/AMD), SDIO offline packs, ARM64 native
scanner, signed installer (needs a paid cert), auto-update with one click.

## Legal (clean-room)

Reimplemented from observed behavior + public docs. No Systweak code, strings,
icons, or driver DB copied. Installer `adusetup*.exe` (v4.6.1086.11084) used only for
behavioral reference via REA static inspection.
