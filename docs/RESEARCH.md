# Competitor research (behavioral, clean-room)

Sources: IObit Driver Booster 14 page, IObit "5 Best Free Driver Updater" blog
(2026-10-09), Advanced Driver Updater site. No competitor code, binaries, text,
or database content is reproduced here or in the app.

## Listed apps and their model

| App | DB claim | Free tier | Core loop |
|---|---|---|---|
| Driver Booster 14 (IObit) | 19M drivers, 1200+ brands | scan free, bulk update gated | version-compare vs cloud DB, WHQL+IObit double check |
| Driver Easy (Easeware) | 8M, OEM + WHQL | scan free, update requires sub | version-compare, multi-version pick |
| Advanced Driver Updater (Systweak) | 600k+ | 1-at-a-time free | version-compare, backup/restore/schedule |
| WinZip Driver Updater | same family as above | 1-at-a-time free | version-compare |
| GetMyDrivers | generic | scan + manual update | version-compare, restore points |

Common denominator: **every one flags outdated-but-working drivers by comparing
installed versions against a driver database.** None of them only handle
broken/missing drivers. That was our gap (we found 1 device; they find dozens).

## Our open-data equivalent

Microsoft Update Catalog instead of a proprietary DB:

- per-HWID search with progressive fallback (full HWID -> VID&PID / VEN&DEV)
- version token extraction from result titles (dates use slashes/colons, so
  dotted tokens are versions)
- strict tuple comparison; incomparable schemes stay quiet, titles shown
- 7-day cache + parallel lookups; user reviews every match before install

## Cross-language duplication (accepted)

Matching logic lives in C (`odu_hw`), Python (`arch.py`/`catalog.py`) and C#
(`ArchUtil`/`Catalog`) independently — no shared runtime. Drift is controlled
by mirrored golden vectors in `native/odu_hw/test_hw.c`, `tests/test_core.py`
and `cs/OduTest/OduTest.cs`. The PowerShell snippets for WU search and VC++
detection are likewise duplicated between `src/odu/*.py` and `cs/OduApp/Ui.cs`;
keep them in sync by hand when either changes.

## Static RE notes (no installation performed)

- `db-setup.exe` (Driver Booster 14.0.1.462, 34MB):
  SHA256 f4a035c4033a686d38cb593607dad32fb009c62a5ca1ea742fb8f42b2717c874.
  PE32 x86, native (not .NET), Inno Setup wrapper (5 markers), full app bundled
  (not a web stub). No plaintext vendor update endpoints in the stub.
- `adusetup*.exe` (Advanced Driver Updater 4.6.1086.11084):
  SHA256 ead50dc309c1465e4375557514ce79e63e23198e82a0f728126c59aff898f6e5.
  PE32 x86, native packed installer stub, 22 PE resources.
- Deliberately NOT installed on the host: both vendors bundle offers/telemetry
  and mutate the driver stack; static + docs analysis answers every question
  the clone needs. Their cloud APIs/DBs are proprietary and were not touched.
