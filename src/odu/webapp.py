"""Stdlib web server + JSON API (no deps, works on x86/x64/ARM64)."""
from __future__ import annotations
import json
import os
import tempfile
import threading
import time
import urllib.parse
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

BASE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UI_DIR = os.path.join(BASE, "ui")
SRC = os.path.join(BASE, "src")
import sys
if SRC not in sys.path:
    sys.path.insert(0, SRC)

JOB: dict = {"running": False, "total": 0, "done": 0, "current": "",
             "results": [], "started": 0.0, "backup": None}


def _job_run(items: list[dict]) -> None:
    from odu import arch as arch_mod
    from odu import backup as backup_mod
    from odu import catalog as catalog_mod
    from odu import installer as installer_mod
    JOB.update(running=True, total=len(items), done=0, current="",
               results=[], started=time.time(), backup=None)
    try:
        dest = os.path.join(os.path.expanduser("~"), "odu-backup")
        JOB["current"] = "Backing up existing drivers…"
        JOB["backup"] = backup_mod.backup_all(dest)
        dl_dir = os.path.join(tempfile.gettempdir(), "odu-dl")
        for it in items:
            name = it.get("device_name", "?")
            JOB["current"] = f"Resolving {name}…"
            res: dict = {"device": name, "hwid": it.get("hwid", ""),
                         "ok": False, "detail": ""}
            try:
                urls = catalog_mod.get_download_urls(it.get("update_id", ""))
                if not urls:
                    res["detail"] = "no CAB published for this update"
                else:
                    JOB["current"] = f"Downloading {name}…"
                    cab = installer_mod.download(urls[0]["url"], dl_dir)
                    JOB["current"] = f"Installing {name}…"
                    inst = installer_mod.install_cab(cab, arch_mod.canonical_arch())
                    res["ok"] = bool(inst.get("ok"))
                    res["detail"] = (inst.get("inf", "") + " :: "
                                     + str(inst.get("log", ""))[-300:])
            except Exception as e:  # keep going through the queue
                res["detail"] = str(e)[-300:]
            JOB["results"].append(res)
            JOB["done"] += 1
            JOB["current"] = ""
    finally:
        JOB["running"] = False
        JOB["current"] = ""


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=UI_DIR, **kw)

    def log_message(self, *a):
        pass

    def _json(self, obj: dict, code: int = 200):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path == "/api/health":
            from odu import arch
            return self._json({"ok": True, "arch": arch.canonical_arch()})
        if parsed.path == "/api/scan":
            from odu import scanner, catalog, arch
            live = "live=1" in (parsed.query or "")
            devs = scanner.scan()
            ups = catalog.resolve_updates(devs, arch.canonical_arch(), live_catalog=live)
            return self._json({"devices": [d.to_dict() for d in devs],
                               "updates": [u.to_dict() for u in ups],
                               "arch": arch.canonical_arch()})
        if parsed.path == "/api/backups":
            from odu import store
            return self._json({"backups": store.list_backups()})
        if parsed.path == "/api/exclusions":
            from odu import store
            return self._json({"exclusions": store.get_exclusions()})
        if parsed.path == "/api/schedule":
            from odu import scheduler
            return self._json(scheduler.status())
        if parsed.path == "/api/job":
            return self._json(JOB)
        return super().do_GET()

    def do_POST(self):
        parsed = urllib.parse.urlparse(self.path)
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length) if length else b"{}"
        try:
            data = json.loads(body or b"{}")
        except Exception:
            data = {}
        if parsed.path == "/api/backup":
            from odu import backup
            dest = data.get("dest") or os.path.join(os.path.expanduser("~"), "odu-backup")
            return self._json(backup.backup_all(dest))
        if parsed.path == "/api/install":
            from odu import backup as bmod
            return self._json(bmod.install_driver(data.get("path", "")))
        if parsed.path == "/api/exclusions/add":
            from odu import store
            store.add_exclusion(data.get("hwid", ""))
            return self._json({"ok": True})
        if parsed.path == "/api/exclusions/remove":
            from odu import store
            store.remove_exclusion(data.get("hwid", ""))
            return self._json({"ok": True})
        if parsed.path == "/api/schedule/enable":
            from odu import scheduler
            return self._json(scheduler.enable_weekly())
        if parsed.path == "/api/schedule/disable":
            from odu import scheduler
            return self._json(scheduler.disable())
        if parsed.path == "/api/update-all":
            if JOB.get("running"):
                return self._json({"ok": False, "error": "a job is already running"}, 409)
            items = [u for u in (data.get("updates") or [])
                     if u.get("update_id")]
            if not items:
                return self._json({"ok": False,
                                    "error": "no live Catalog updates — run a Live scan first"}, 400)
            threading.Thread(target=_job_run, args=(items,), daemon=True).start()
            return self._json({"ok": True, "total": len(items)})
        return self._json({"ok": False, "error": "unknown route"}, 404)


def serve(port: int = 8901) -> None:
    srv = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    print(f"Open Driver Updater UI: http://127.0.0.1:{port}/")
    try:
        import webbrowser
        webbrowser.open(f"http://127.0.0.1:{port}/")
    except Exception:
        pass
    srv.serve_forever()


if __name__ == "__main__":
    serve(int(sys.argv[1]) if len(sys.argv) > 1 else 8901)
