"""End-to-end tests for the web API.

Starts the real stdlib server (python -m odu.cli serve) on a loopback port,
hits every GET/POST route, and asserts shapes. No network calls: the live
Catalog toggle is off, so scan returns problem devices without hitting the
network.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.error
import urllib.request
import urllib.error

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


if __name__ == "__main__":
    unittest.main()
