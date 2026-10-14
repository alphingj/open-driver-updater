"""One-click driver install: download CAB -> expand -> arch-filter INF -> pnputil.

Stdlib-only. Requires admin for the pnputil step; download/expand work as user.
"""
from __future__ import annotations
import os
import re
import subprocess
import tempfile
import urllib.request
from .arch import canonical_arch, is_arch_compatible, pnputil_path


def download(url: str, dest_dir: str, timeout: int = 120) -> str:
    os.makedirs(dest_dir, exist_ok=True)
    name = (url.split("?")[0].rsplit("/", 1)[-1] or "driver.cab")[-80:]
    if not name.lower().endswith(".cab"):
        name += ".cab"
    dest = os.path.join(dest_dir, name)
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=timeout) as r, open(dest, "wb") as f:
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            f.write(chunk)
    return dest


def expand_cab(cab: str, dest_dir: str) -> list[str]:
    os.makedirs(dest_dir, exist_ok=True)
    try:
        p = subprocess.run(["expand.exe", cab, "-F:*", dest_dir],
                           capture_output=True, text=True, timeout=300)
        if p.returncode != 0:
            return []
    except Exception:
        return []
    found = []
    for root, _, files in os.walk(dest_dir):
        for fn in files:
            if fn.lower().endswith(".inf"):
                found.append(os.path.join(root, fn))
    return found


def inf_models_text(inf_path: str) -> str:
    try:
        with open(inf_path, "rb") as f:
            raw = f.read(65536)
        return raw.decode("utf-16", "ignore") if raw[:2] in (b"\xff\xfe", b"\xfe\xff") \
            else raw.decode("utf-8", "ignore")
    except Exception:
        return ""


def pick_inf(infs: list[str], arch: str) -> str:
    """Prefer an INF whose Manufacturer section matches this arch."""
    for inf in infs:
        text = inf_models_text(inf)
        mans = re.findall(r"\[Manufacturer([^\]]*)\]", text, re.I)
        if not mans:
            continue
        if any(is_arch_compatible("[" + m + "]", arch) for m in mans):
            return inf
    return infs[0] if infs else ""


def install_inf(inf: str) -> dict:
    cmd = [pnputil_path(), "/add-driver", inf, "/install"]
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
        return {"ok": p.returncode == 0, "log": (p.stdout + p.stderr)[-2000:]}
    except Exception as e:
        return {"ok": False, "log": str(e)}


def install_cab(cab: str, arch: str | None = None, workdir: str | None = None) -> dict:
    """Expand a Catalog CAB and install the best-arch INF. Returns result dict."""
    arch = canonical_arch(arch)
    base = workdir or tempfile.mkdtemp(prefix="odu-")
    out = os.path.join(base, "expanded")
    infs = expand_cab(cab, out)
    if not infs:
        return {"ok": False, "inf": "", "log": "no INF found in CAB"}
    inf = pick_inf(infs, arch)
    res = install_inf(inf)
    res["inf"] = inf
    return res
