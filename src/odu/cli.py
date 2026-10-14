"""CLI: odu scan [--live] | odu backup | odu update-all [--live] | odu serve."""
from __future__ import annotations
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "src"))


def cmd_scan(args) -> int:
    from odu import scanner, catalog, arch
    devs = scanner.scan()
    updates = catalog.resolve_updates(devs, arch.native_arch(),
                                      live_catalog=args.live)
    print(json.dumps({"devices": [d.to_dict() for d in devs],
                      "updates": [u.to_dict() for u in updates]}, indent=2))
    return 0


def cmd_backup(args) -> int:
    from odu import backup
    print(json.dumps(backup.backup_all(args.dest), indent=2))
    return 0


def cmd_update_all(args) -> int:
    from odu import scanner, catalog, arch
    from odu.webapp import JOB, _job_run
    devs = scanner.scan()
    updates = catalog.resolve_updates(devs, arch.native_arch(), live_catalog=True)
    items = [u.to_dict() for u in updates if u.update_id]
    if not items:
        print(json.dumps({"ok": False,
                          "error": "no live Catalog updates found"}, indent=2))
        return 1
    _job_run(items)
    print(json.dumps({"ok": True, "backup": JOB.get("backup"),
                      "results": JOB.get("results")}, indent=2))
    return 0 if all(r.get("ok") for r in JOB.get("results", [])) else 1


def cmd_serve(args) -> int:
    from odu.webapp import serve
    serve(args.port)
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="odu", description="Open Driver Updater (free)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("scan", help="scan devices and resolve updates")
    p.add_argument("--live", action="store_true", help="live MS Catalog lookup")
    p.set_defaults(fn=cmd_scan)
    s = sub.add_parser("serve", help="start Apple-style web UI")
    s.add_argument("--port", type=int, default=8901)
    s.set_defaults(fn=cmd_serve)
    b = sub.add_parser("backup", help="export all drivers")
    b.add_argument("--dest", default=os.path.join(os.path.expanduser("~"), "odu-backup"))
    b.set_defaults(fn=cmd_backup)
    u = sub.add_parser("update-all", help="backup, then download+install all live updates")
    u.set_defaults(fn=cmd_update_all)
    args = ap.parse_args(argv)
    return int(args.fn(args) or 0)


if __name__ == "__main__":
    raise SystemExit(main())
