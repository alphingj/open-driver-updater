"""Stdlib web server + JSON API (no deps, works on x86/x64/ARM64).

Listens on 127.0.0.1 only: any local process can call it, so treat the API as
local-trust. Driver installs additionally require the server to run elevated.
"""
from __future__ import annotations
import json
import os
import re
import tempfile
import threading
import time
import urllib.parse
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

import sys
if os.path.join(os.path.dirname(os.path.abspath(__file__)), "..") not in sys.path:
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

BASE = os.path.dirname(os.path.abspath(__file__))


def _find_ui_dir() -> str:
    here = os.path.abspath(__file__)
    for _ in range(5):
        here = os.path.dirname(here)
        if os.path.isfile(os.path.join(here, "ui", "index.html")):
            return os.path.join(here, "ui")
    try:
        from importlib.resources import files
        cand = files("odu").joinpath("ui")
        if cand.is_dir():
            return str(cand)
    except Exception:
        pass
    return os.path.join(BASE, "..", "..", "ui")


UI_DIR = _find_ui_dir()
_GUID_RE = re.compile(r"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-"
                      r"[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\Z")

_JOB_LOCK = threading.Lock()
JOB: dict = {"running": False, "total": 0, "done": 0, "current": "",
             "results": [], "started": 0.0, "backup": None, "cancel": False}

_SCAN_LOCK = threading.Lock()
_SCAN_CACHE: dict = {"at": 0.0, "key": "", "body": None}
SCAN_TTL = 60.0


def job_snapshot() -> dict:
    with _JOB_LOCK:
        return {"running": JOB["running"], "total": JOB["total"], "done": JOB["done"],
                "current": JOB["current"], "results": list(JOB["results"]),
                "started": JOB["started"], "backup": JOB["backup"],
                "restore": JOB.get("restore")}


def _dl_progress(name: str, done: int, total: int) -> None:
    with _JOB_LOCK:
        if total > 0:
            pct = min(100, done * 100 // total)
            JOB["current"] = f"Downloading {name}… {pct}%"
        else:
            JOB["current"] = f"Downloading {name}… {done // 1024} KB"


def _job_run(items: list[dict]) -> None:
    from odu import arch as arch_mod
    from odu import backup as backup_mod
    from odu import catalog as catalog_mod
    from odu import installer as installer_mod
    from odu import safety as safety_mod
    with _JOB_LOCK:
        JOB.update(running=True, total=len(items) + 2, done=0, current="",
                   results=[], started=time.time(), backup=None, cancel=False,
                   restore=None)
    try:
        with _JOB_LOCK:
            JOB["current"] = "Creating system restore point…"
        restore_res = safety_mod.create_restore_point()
        with _JOB_LOCK:
            JOB["restore"] = restore_res
            JOB["done"] += 1
            JOB["results"].append({"device": "System restore point",
                                   "hwid": "",
                                   "ok": bool(restore_res.get("ok")),
                                   "detail": str(restore_res.get("log", ""))})
        dest = os.path.join(os.path.expanduser("~"), "odu-backup")
        with _JOB_LOCK:
            JOB["current"] = "Backing up existing drivers…"
        backup_res = backup_mod.backup_all(dest)
        with _JOB_LOCK:
            JOB["backup"] = backup_res
            JOB["done"] += 1
            JOB["results"].append({"device": "Pre-update backup",
                                   "hwid": "",
                                   "ok": bool(backup_res.get("ok")),
                                   "detail": str(backup_res.get("path", ""))})
        if not backup_res.get("ok"):
            with _JOB_LOCK:
                JOB["results"].append({"device": "Update queue",
                                       "hwid": "",
                                       "ok": False,
                                       "detail": "stopped: backup failed, no drivers were installed. "
                                                 + str(backup_res.get("log", ""))[-200:]})
                JOB["done"] = JOB["total"]
            return
        dl_dir = os.path.join(tempfile.gettempdir(), "odu-dl")
        arch = arch_mod.native_arch()
        for it in items:
            name = it.get("device_name", "?")
            res: dict = {"device": name, "hwid": it.get("hwid", ""),
                         "ok": False, "detail": ""}
            try:
                with _JOB_LOCK:
                    JOB["current"] = f"Resolving {name}…"
                urls = catalog_mod.get_download_urls(it.get("update_id", ""),
                                                     it.get("size", 0))
                if not urls:
                    res["detail"] = "no CAB published for this update"
                else:
                    ok_all, logs, published = True, [], []
                    n_files = len(urls)
                    for n, u in enumerate(urls, 1):
                        with _JOB_LOCK:
                            JOB["current"] = (f"Downloading {name}…"
                                              if n_files == 1 else
                                              f"Downloading {name} ({n}/{n_files})…")
                        cab = installer_mod.download(
                            u["url"], dl_dir,
                            expected_sha256_b64=u.get("sha256", ""),
                            progress=lambda done, total, name=name: _dl_progress(name, done, total))
                        with _JOB_LOCK:
                            JOB["current"] = f"Installing {name}…"
                        inst = installer_mod.install_cab(cab, arch)
                        ok_all = ok_all and bool(inst.get("ok"))
                        if inst.get("published"):
                            published.append(inst["published"])
                        logs.append(str(inst.get("inf", "")) + " :: "
                                    + str(inst.get("log", ""))[-200:])
                    res["ok"] = ok_all
                    res["published"] = ",".join(published)
                    res["detail"] = " ||| ".join(logs)[-2000:]
                    if ok_all and published:
                        from odu import store as store_mod
                        for oem in published:
                            store_mod.record_install(oem, it.get("instance_id", ""), name)
            except Exception as e:  # keep going through the queue
                res["detail"] = str(e)[-300:]
            with _JOB_LOCK:
                JOB["results"].append(res)
                JOB["done"] += 1
                JOB["current"] = ""
                if JOB.get("cancel"):
                    JOB["results"].append({"device": "Update queue", "hwid": "",
                                           "ok": False,
                                           "detail": "cancelled; remaining drivers untouched"})
                    break
    finally:
        with _JOB_LOCK:
            JOB["running"] = False
            JOB["current"] = ""
        with _SCAN_LOCK:
            _SCAN_CACHE.update(at=0.0, key="", body=None)


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=UI_DIR, **kw)

    def log_message(self, *a):
        pass

    def _json(self, obj: dict, code: int = 200):
        try:
            body = json.dumps(obj).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def _scan_body(self, live: bool) -> dict:
        from odu import scanner, catalog, arch
        key = "live" if live else "plain"
        now = time.time()
        with _SCAN_LOCK:
            if _SCAN_CACHE["body"] is not None and _SCAN_CACHE["key"] == key \
                    and now - _SCAN_CACHE["at"] < SCAN_TTL:
                body = dict(_SCAN_CACHE["body"])
                body["cached"] = True
                return body
        devs = scanner.scan()
        ups = catalog.resolve_updates(devs, arch.native_arch(), live_catalog=live)
        body = {"devices": [d.to_dict() for d in devs],
                "updates": [u.to_dict() for u in ups],
                "arch": arch.native_arch(), "cached": False}
        with _SCAN_LOCK:
            _SCAN_CACHE.update(at=now, key=key, body=body)
        return body

    def do_GET(self):
        try:
            parsed = urllib.parse.urlparse(self.path)
            if parsed.path == "/api/health":
                from odu import arch
                from odu import __version__
                return self._json({"ok": True, "arch": arch.native_arch(),
                                   "version": __version__,
                                   "ui": os.path.isfile(os.path.join(UI_DIR, "index.html"))})
            if parsed.path == "/api/scan":
                live = "live=1" in (parsed.query or "")
                return self._json(self._scan_body(live))
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
                return self._json(job_snapshot())
            if parsed.path == "/api/system":
                from odu import safety as safety_mod
                return self._json({"reboot": safety_mod.reboot_pending()})
            if parsed.path == "/api/app-update":
                return self._json(_app_update())
            if parsed.path == "/api/components":
                from odu import components
                return self._json({"components": components.check()})
            if parsed.path == "/api/wu":
                from odu import wu
                return self._json({"updates": wu.search()})
            if parsed.path == "/api/drivers":
                from odu import drivers as drivers_mod
                return self._json({"drivers": drivers_mod.enum_drivers()})
            if parsed.path == "/api/procs":
                from odu import gameboost
                return self._json({"procs": gameboost.top_processes()})
            return super().do_GET()
        except (BrokenPipeError, ConnectionResetError):
            pass

    def do_POST(self):
        try:
            return self._do_post()
        except (BrokenPipeError, ConnectionResetError):
            pass

    def _do_post(self):
        parsed = urllib.parse.urlparse(self.path)
        try:
            length = int(self.headers.get("Content-Length") or 0)
        except (TypeError, ValueError):
            length = 0
        body = self.rfile.read(length) if length > 0 else b"{}"
        try:
            data = json.loads(body or b"{}")
        except Exception:
            data = {}
        if not isinstance(data, dict):
            data = {}
        if parsed.path == "/api/backup":
            from odu import backup
            dest = data.get("dest") or os.path.join(os.path.expanduser("~"), "odu-backup")
            return self._json(backup.backup_all(str(dest)))
        if parsed.path == "/api/cancel":
            with _JOB_LOCK:
                JOB["cancel"] = True
            return self._json({"ok": True})
        if parsed.path == "/api/exclusions/add":
            from odu import store
            store.add_exclusion(str(data.get("hwid", "")))
            return self._json({"ok": True})
        if parsed.path == "/api/exclusions/remove":
            from odu import store
            store.remove_exclusion(str(data.get("hwid", "")))
            return self._json({"ok": True})
        if parsed.path == "/api/schedule/enable":
            from odu import scheduler
            return self._json(scheduler.enable_weekly())
        if parsed.path == "/api/schedule/disable":
            from odu import scheduler
            return self._json(scheduler.disable())
        if parsed.path == "/api/update-all":
            with _JOB_LOCK:
                running = JOB["running"]
            if running:
                return self._json({"ok": False, "error": "a job is already running"}, 409)
            raw = data.get("updates") or []
            items = [u for u in raw
                     if isinstance(u, dict) and _GUID_RE.match(str(u.get("update_id", "")))
                     and str(u.get("confidence", "high")) == "high"]
            if not items:
                return self._json({"ok": False,
                                    "error": "no high-confidence Catalog updates — review items need manual install"}, 400)
            threading.Thread(target=_job_run, args=(items,), daemon=True).start()
            return self._json({"ok": True, "total": len(items)})
        if parsed.path == "/api/rollback":
            from odu import installer as installer_mod
            from odu import store as store_mod
            oems = [o.strip() for o in str(data.get("published", "")).split(",") if o.strip()]
            # Only packages THIS app installed may be rolled back.
            alien = [o for o in oems if not store_mod.installed_by_odu(o)]
            if alien:
                return self._json({"ok": False,
                                    "log": "refusing: not installed by this app: "
                                           + ", ".join(alien)}, 403)
            outs = [installer_mod.rollback_driver(o) for o in oems]
            ok = bool(outs) and all(o.get("ok") for o in outs)
            if ok:
                for o in oems:
                    store_mod.forget_install(o)
            return self._json({"ok": ok,
                               "log": " | ".join(str(o.get("log", "")) for o in outs)[-2000:]})
        if parsed.path == "/api/update-one":
            with _JOB_LOCK:
                running = JOB["running"]
            if running:
                return self._json({"ok": False, "error": "a job is already running"}, 409)
            u = data.get("update") or {}
            if not (isinstance(u, dict) and _GUID_RE.match(str(u.get("update_id", "")))):
                return self._json({"ok": False,
                                    "error": "needs a live Catalog update"}, 400)
            threading.Thread(target=_job_run, args=([u],), daemon=True).start()
            return self._json({"ok": True, "total": 1})
        if parsed.path == "/api/wu-install":
            from odu import wu
            titles = data.get("titles") or []
            return self._json(wu.install([str(t) for t in titles]))
        if parsed.path == "/api/report":
            from odu import scanner, catalog, arch
            devs = scanner.scan()
            ups = catalog.resolve_updates(devs, arch.native_arch(),
                                          live_catalog=bool(data.get("live")))
            return self._json({"report": _html_report(devs, ups)})
        if parsed.path == "/api/folder-install":
            from odu import installer as installer_mod
            from odu import arch as arch_mod
            from odu import safety as safety_mod
            folder = str(data.get("folder", ""))
            if not os.path.exists(folder):
                return self._json({"ok": False, "log": "path does not exist"}, 400)
            # Same safety bar as one-click installs: restore point first.
            # (Full driver backup stays a separate explicit step here.)
            rp = safety_mod.create_restore_point("Open Driver Updater folder install")
            if os.path.isfile(folder) and folder.lower().endswith(".cab"):
                out = installer_mod.install_cab(folder, arch_mod.native_arch())
            else:
                # pnputil /add-driver accepts a directory of INFs as well as one INF
                out = installer_mod.install_inf(folder)
            out["restore"] = rp.get("log", "")
            return self._json(out)
        if parsed.path == "/api/backup-one":
            from odu import drivers as drivers_mod
            dest = data.get("dest") or os.path.join(os.path.expanduser("~"), "odu-backup")
            return self._json(drivers_mod.export_driver(str(data.get("oem", "")), str(dest)))
        if parsed.path == "/api/kill":
            from odu import gameboost
            return self._json(gameboost.kill(data.get("pid", 0)))
        return self._json({"ok": False, "error": "unknown route"}, 404)


_APP_UPDATE_CACHE: dict = {"at": 0.0, "body": None}


def _app_update() -> dict:
    from odu import __version__
    now = time.time()
    if _APP_UPDATE_CACHE["body"] is not None and now - _APP_UPDATE_CACHE["at"] < 86400:
        return _APP_UPDATE_CACHE["body"]
    body: dict = {"current": __version__, "latest": __version__, "url": "",
                  "update": False}
    try:
        req = urllib.request.Request(
            "https://api.github.com/repos/alphingj/open-driver-updater/releases/latest",
            headers={"User-Agent": "open-driver-updater", "Accept": "application/vnd.github+json"})
        with urllib.request.urlopen(req, timeout=15) as r:
            rel = json.loads(r.read(65536).decode("utf-8", "ignore"))
        latest = str(rel.get("tag_name", ""))
        if latest.lower().startswith("v"):
            latest = latest[1:]
        if latest and latest != __version__:
            body = {"current": __version__, "latest": latest,
                    "url": str(rel.get("html_url", "")), "update": True}
    except Exception:
        pass
    _APP_UPDATE_CACHE.update(at=now, body=body)
    return body


def _html_report(devs: list, ups: list) -> str:
    import html as html_mod
    from odu import arch

    def esc(x: object) -> str:
        return html_mod.escape(str(x or ""))

    rows = "\n".join(
        f"<tr><td>{esc(d.friendly_name or d.instance_id)}</td>"
        f"<td>{esc((d.hwid or [''])[0])}</td>"
        f"<td>{esc(d.driver_version or 'none')}</td><td>{esc(d.status)}</td></tr>"
        for d in devs)
    urows = "\n".join(
        f"<tr><td>{esc(u.device_name)}</td><td>{esc(u.hwid)}</td>"
        f"<td>{esc(u.installed_version or 'none')}</td>"
        f"<td>{esc(u.available_version)}</td>"
        f"<td>{esc(u.confidence)}</td></tr>"
        for u in ups)
    return (f"<!doctype html><html><head><meta charset='utf-8'>"
            f"<title>Open Driver Updater report</title></head><body>"
            f"<h1>Open Driver Updater report ({esc(arch.native_arch())})</h1>"
            f"<h2>Updates ({len(ups)})</h2><table border='1'>{urows}</table>"
            f"<h2>Devices ({len(devs)})</h2><table border='1'>{rows}</table>"
            f"</body></html>")


def serve(port: int = 8901) -> None:
    try:
        srv = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    except OSError:
        print(f"Port {port} is already in use — is Open Driver Updater already running?")
        return
    print(f"Open Driver Updater UI: http://127.0.0.1:{port}/")
    try:
        import webbrowser
        webbrowser.open(f"http://127.0.0.1:{port}/")
    except Exception:
        pass
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    serve(int(sys.argv[1]) if len(sys.argv) > 1 else 8901)
