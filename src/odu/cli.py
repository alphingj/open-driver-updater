"""CLI: odu scan/list/backup/schedule/serve."""
from __future__ import annotations
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "src"))


def cmd_scan(args) -> int:
    from odu import scanner, catalog, arch
    devs = scanner.scan()
    updates = catalog.resolve_updates(devs, arch.canonical_arch(),
                                      live_catalog=args.live)
    print(json.dumps({"devices": [d.to_dict() for d in devs],
                      "updates": [u.to_dict() for u in updates]}, indent=2))
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="odu", description="Open Driver Updater (free)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("scan", help="scan devices and resolve updates")
    p.add_argument("--live", action="store_true", help="live MS Catalog lookup")
    p.set_defaults(fn=cmd_scan)
    s = sub.add_parser("serve", help="start Apple-style web UI")
    s.add_argument("--port", type=int, default=8901)
    s.set_defaults(fn=lambda a: (__import__("odu.webapp", fromlist=["serve"]).serve(a.port), 0)[1])
    b = sub.add_parser("backup", help="export all drivers")
    b.add_argument("--dest", default=os.path.join(os.path.expanduser("~"), "odu-backup"))
    b.set_defaults(fn=lambda a: print(__import__("odu.backup", fromlist=["backup_all"]).backup_all(a.dest)) or 0)
    args = ap.parse_args(argv)
    return int(args.fn(args) or 0)


if __name__ == "__main__":
    raise SystemExit(main())
