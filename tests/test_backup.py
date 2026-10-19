"""End-to-end tests for the driver backup path.

Calls the real pnputil /export-driver and verifies a directory with files
is produced. Runs against the machine's actual driver store, so it may take
a minute on driver-heavy machines.
"""
from __future__ import annotations

import os
import sys
import tempfile
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "src"))

from odu import backup


class BackupTest(unittest.TestCase):
    def test_backup_all_creates_directory(self):
        with tempfile.TemporaryDirectory() as d:
            dest_root = os.path.join(d, "odu-backup")
            r = backup.backup_all(dest_root)
            self.assertTrue(r["ok"], msg=r.get("log", ""))
            self.assertTrue(os.path.isdir(r["path"]), r["path"])
            files = os.listdir(r["path"])
            self.assertGreater(len(files), 0, "expected at least one exported .inf")


if __name__ == "__main__":
    unittest.main()
