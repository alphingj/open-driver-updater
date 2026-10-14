"""One-click driver install: download CAB -> verify -> expand -> arch INF -> pnputil.

Stdlib-only. Download/expand run as user; the pnputil step needs elevation.
CAB payloads come from Microsoft Update Catalog; pnputil itself enforces driver
signatures at install time.
"""
from __future__ import annotations
import base64
import hashlib
import os
import shutil
import subprocess
import tempfile
import time
import urllib.request
from .arch import canonical_arch, expand_path, is_inf_compatible, pnputil_path


def download(url: str, dest_dir: str, timeout: int = 120,
             expected_sha256_b64: str = "",
             progress=None) -> str:
    """Download a CAB, optionally verifying its base64 SHA-256 (Catalog digest)."""
    os.makedirs(dest_dir, exist_ok=True)
    name = (url.split("?")[0].rsplit("/", 1)[-1] or "driver.cab")[-80:]
    if not name.lower().endswith(".cab"):
        name += ".cab"
    dest = os.path.join(dest_dir, name)
    digest = hashlib.sha256()
    deadline = time.time() + max(timeout, 600)
    last_error: Exception | None = None
    for _attempt in (1, 2):  # one automatic retry on transient network errors
        digest = hashlib.sha256()
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(req, timeout=timeout) as r, open(dest, "wb") as f:
                try:
                    total = int(r.headers.get("Content-Length") or 0)
                except (TypeError, ValueError):
                    total = 0
                done = 0
                while True:
                    if time.time() > deadline:
                        raise TimeoutError("download exceeded overall deadline")
                    chunk = r.read(1 << 20)
                    if not chunk:
                        break
                    digest.update(chunk)
                    f.write(chunk)
                    done += len(chunk)
                    if progress:
                        try:
                            progress(done, total)
                        except Exception:
                            pass
            break
        except ValueError:
            raise
        except Exception as e:
            last_error = e
            continue
    else:
        raise last_error or OSError("download failed")
    if expected_sha256_b64:
        try:
            expected = base64.b64decode(expected_sha256_b64)
        except Exception:
            expected = b""
        if expected and digest.digest() != expected:
            os.remove(dest)
            raise ValueError("CAB SHA-256 mismatch — download rejected")
    return dest


def expand_cab(cab: str, dest_dir: str) -> list[str]:
    os.makedirs(dest_dir, exist_ok=True)
    try:
        p = subprocess.run([expand_path(), cab, "-F:*", dest_dir],
                           capture_output=True, text=True, timeout=300)
        if p.returncode != 0:
            return []
    except Exception:
        return []
    return [os.path.join(root, fn)
            for root, _, files in os.walk(dest_dir) for fn in files
            if fn.lower().endswith(".inf")]


def inf_models_text(inf_path: str, limit: int = 1 << 20) -> str:
    try:
        with open(inf_path, "rb") as f:
            raw = f.read(limit)
        if raw[:2] in (b"\xff\xfe", b"\xfe\xff"):
            return raw.decode("utf-16", "ignore")
        try:
            return raw.decode("utf-8")
        except UnicodeDecodeError:
            return raw.decode("mbcs", "ignore")
    except Exception:
        return ""


def pick_inf(infs: list[str], arch: str) -> str:
    """Return the first INF with an arch-compatible Manufacturer section.

    Returns "" when nothing matches (caller must not fall back to a wrong-arch
    INF — pnputil would reject it, but failing early gives a clearer message).
    """
    for inf in infs:
        if is_inf_compatible(inf_models_text(inf), arch):
            return inf
    return ""


def install_inf(inf: str) -> dict:
    cmd = [pnputil_path(), "/add-driver", inf, "/install"]
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
        log = (p.stdout + p.stderr)[-2000:]
        return {"ok": p.returncode == 0, "log": log,
                "published": _published_name(log)}
    except Exception as e:
        return {"ok": False, "log": str(e), "published": ""}


def _published_name(log: str) -> str:
    """Parse 'Published name : oemNN.inf' for later rollback."""
    import re
    m = re.search(r"Published name\s*:\s*(oem\d+\.inf)", log, re.I)
    if m:
        return m.group(1).lower()
    # Non-English pnputil localizes the label: fall back to any oemNN token.
    m = re.search(r"\b(oem\d+\.inf)\b", log, re.I)
    return m.group(1).lower() if m else ""


def rollback_driver(published: str) -> dict:
    """Remove a driver we installed (pnputil /delete-driver oemNN /uninstall).

    Windows falls back to the previously installed driver. Only call with
    published names recorded by install_cab/install_inf.
    """
    import re
    if not re.fullmatch(r"oem\d+\.inf", (published or "").lower()):
        return {"ok": False, "log": "refusing: not a recorded oem name"}
    cmd = [pnputil_path(), "/delete-driver", published.lower(), "/uninstall"]
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
        return {"ok": p.returncode == 0, "log": (p.stdout + p.stderr)[-2000:]}
    except Exception as e:
        return {"ok": False, "log": str(e)}


def install_cab(cab: str, arch: str | None = None, workdir: str | None = None,
                cleanup: bool = True) -> dict:
    """Expand a Catalog CAB and install the best-arch INF. Returns result dict."""
    arch = canonical_arch(arch)
    base = workdir or tempfile.mkdtemp(prefix="odu-")
    try:
        infs = expand_cab(cab, os.path.join(base, "expanded"))
        if not infs:
            return {"ok": False, "inf": "", "log": "no INF found in CAB"}
        inf = pick_inf(infs, arch)
        if not inf:
            return {"ok": False, "inf": "",
                    "log": f"CAB has {len(infs)} INF(s) but none target {arch}"}
        res = install_inf(inf)
        res["inf"] = inf
        return res
    finally:
        if cleanup and not workdir:
            shutil.rmtree(base, ignore_errors=True)
