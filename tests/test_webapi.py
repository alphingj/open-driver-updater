"""End-to-end tests for the web API.

Starts the real stdlib server (python -m odu.cli serve) on a loopback port,
hits every GET/POST route, and asserts shapes. No network calls: the live
Catalog toggle is off, so scan returns problem devices without hitting the
network.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "src"))

PORT = 8999  # avoid colliding with a dev server the user may have running
BASE = f"http://127.0.0.1:{PORT}"


def _req(method, path, body=None, timeout=30):
    data = None
    headers = {}
    if body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            body = e.read().decode("utf-8")
            try:
                return e.code, json.loads(body)
            except Exception:
                return e.code, {"ok": False, "error": body[:200]}
        except Exception:
            return e.code, {"ok": False, "error": str(e)}


class WebApiTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Hermetic DB: the suite writes exclusions/backups, so point both the
        # server and this process at a temp database instead of ~/.odu.sqlite3.
        cls.tmp = tempfile.mkdtemp(prefix="odu-webapi-")
        cls._old_db = os.environ.get("ODU_DB")
        os.environ["ODU_DB"] = os.path.join(cls.tmp, "test.db")
        cls.proc = subprocess.Popen(
            [sys.executable, "-m", "odu.cli", "serve", "--port", str(PORT)],
            cwd=ROOT,
            env={**os.environ, "PYTHONPATH": os.path.join(ROOT, "src")},
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        # Wait for the server to answer.
        cls._proc = cls.proc
        deadline = time.time() + 60
        while True:
            try:
                urllib.request.urlopen(BASE + "/api/health", timeout=2)
                return
            except Exception:
                if time.time() > deadline:
                    cls.proc.kill()
                    raise RuntimeError("web server did not start in 60s")
                time.sleep(1)

    @classmethod
    def tearDownClass(cls):
        cls.proc.terminate()
        try:
            cls.proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            cls.proc.kill()
        if cls._old_db is None:
            os.environ.pop("ODU_DB", None)
        else:
            os.environ["ODU_DB"] = cls._old_db
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def test_health(self):
        code, j = _req("GET", "/api/health")
        self.assertEqual(code, 200)
        self.assertTrue(j["ok"])
        self.assertIn(j["arch"], ("x86", "x64", "arm64"))
        self.assertTrue(j["ui"])

    def test_scan_returns_devices(self):
        code, j = _req("GET", "/api/scan")
        self.assertEqual(code, 200)
        self.assertIsInstance(j["devices"], list)
        self.assertGreater(len(j["devices"]), 0)
        d = j["devices"][0]
        for key in ("instance_id", "friendly_name", "status", "hwid"):
            self.assertIn(key, d)

    def test_scan_arch(self):
        code, j = _req("GET", "/api/scan")
        self.assertEqual(code, 200)
        self.assertIn(j["arch"], ("x86", "x64", "arm64"))

    def test_backups_endpoint(self):
        code, j = _req("GET", "/api/backups")
        self.assertEqual(code, 200)
        self.assertIsInstance(j["backups"], list)

    def test_installs_endpoint(self):
        code, j = _req("GET", "/api/installs")
        self.assertEqual(code, 200)
        self.assertIsInstance(j["installs"], list)

    def test_rollback_refuses_unknown(self):
        code, j = _req("POST", "/api/rollback", {"published": "oem999998.inf"})
        self.assertEqual(code, 403)
        self.assertFalse(j["ok"])
        self.assertIn("not installed by this app", j["log"])

    def test_exclusions_round_trip(self):
        # empty initially
        code, j = _req("GET", "/api/exclusions")
        self.assertEqual(code, 200)
        before = list(j["exclusions"])

        test_hwid = "PCI\\VEN_TEST&DEV_0001"
        code, j = _req("POST", "/api/exclusions/add", {"hwid": test_hwid})
        self.assertEqual(code, 200)
        self.assertTrue(j["ok"])

        code, j = _req("GET", "/api/exclusions")
        self.assertEqual(code, 200)
        self.assertIn(test_hwid, j["exclusions"])

        code, j = _req("POST", "/api/exclusions/remove", {"hwid": test_hwid})
        self.assertEqual(code, 200)
        self.assertTrue(j["ok"])

        code, j = _req("GET", "/api/exclusions")
        self.assertEqual(code, 200)
        self.assertEqual(j["exclusions"], before)

    def test_schedule_endpoint(self):
        code, j = _req("GET", "/api/schedule")
        self.assertEqual(code, 200)
        self.assertIn("enabled", j)

    def test_job_idle(self):
        code, j = _req("GET", "/api/job")
        self.assertEqual(code, 200)
        self.assertFalse(j["running"])

    def test_unknown_route(self):
        code, j = _req("GET", "/api/no-such-route")
        self.assertEqual(code, 404)
        self.assertFalse(j["ok"])

    def test_update_all_no_live(self):
        # No live scan has been run → should tell us to run Live first.
        code, j = _req("POST", "/api/update-all", {"updates": []})
        self.assertEqual(code, 400)
        self.assertFalse(j["ok"])

    def test_cancel_idle(self):
        code, j = _req("POST", "/api/cancel", {})
        self.assertEqual(code, 200)
        self.assertTrue(j["ok"])

    def test_oversized_body_rejected(self):
        import http.client
        conn = http.client.HTTPConnection("127.0.0.1", PORT, timeout=10)
        try:
            conn.putrequest("POST", "/api/cancel")
            conn.putheader("Content-Length", str(9 * 1024 * 1024))
            conn.putheader("Content-Type", "application/json")
            conn.endheaders()
            r = conn.getresponse()
            body = json.loads(r.read().decode())
            self.assertEqual(r.status, 413)
            self.assertFalse(body["ok"])
        finally:
            conn.close()

    def test_concurrent_polling_and_store_writes(self):
        # The UI polls while a job writes; WAL + short connections must keep
        # every request a clean 200 (no "database is locked").
        from odu import store
        errors = []

        def reader():
            for _ in range(6):
                for path in ("/api/health", "/api/backups", "/api/installs",
                             "/api/exclusions", "/api/job"):
                    try:
                        code, _ = _req("GET", path, timeout=20)
                        if code != 200:
                            errors.append((path, code))
                    except Exception as e:
                        errors.append((path, str(e)))

        def writer():
            try:
                for i in range(30):
                    store.record_backup("ID%d" % i, "concurrency", "C:\\tmp")
            except Exception as e:
                errors.append(("write", str(e)))

        threads = [threading.Thread(target=reader) for _ in range(6)]
        threads += [threading.Thread(target=writer) for _ in range(2)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()
        self.assertEqual(errors, [])


if __name__ == "__main__":
    unittest.main()
