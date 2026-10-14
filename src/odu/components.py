"""Game/runtime components: VC++ redists + DirectX (open Microsoft links)."""
from __future__ import annotations
import os
import re
import sys

VC_X64 = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
VC_X86 = "https://aka.ms/vs/17/release/vc_redist.x86.exe"
VC_ARM64 = "https://aka.ms/vs/17/release/vc_redist.arm64.exe"
DX_SETUP = "https://www.microsoft.com/en-us/download/details.aspx?id=35"


def _uninstall_names() -> list[str]:
    names: list[str] = []
    if sys.platform != "win32":
        return names
    try:
        import winreg
    except ImportError:
        return names
    seen_roots = set()
    # One root × three registry views covers 32/64-bit hives. (The WOW6432Node
    # path is the same data as the 32-bit view — listing both double-counts.)
    for access in (0, 0x0100, 0x0200):  # default, 64-bit, 32-bit views
        for root in ("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",):
            if (root, access) in seen_roots:
                continue
            seen_roots.add((root, access))
            try:
                with winreg.OpenKey(0x80000002, root, 0,
                                    winreg.KEY_READ | access) as base:
                    i = 0
                    while True:
                        try:
                            sub = winreg.EnumKey(base, i)
                        except OSError:
                            break
                        i += 1
                        try:
                            with winreg.OpenKey(base, sub) as k:
                                v, _ = winreg.QueryValueEx(k, "DisplayName")
                                names.append(str(v))
                        except OSError:
                            continue
            except OSError:
                continue
    return names


def _has_current_vc(names: list[str]) -> bool:
    """True when a 2015-or-newer (v14+) VC++ redistributable is installed.

    Any old redist (e.g. 2010) does NOT satisfy games built against v14+.
    """
    for n in names:
        if "Microsoft Visual C++" not in n or "Redistributable" not in n:
            continue
        if re.search(r"\b(201[5-9]|20[2-9]\d)\b", n) or "14." in n:
            return True
    return False


def check() -> list[dict]:
    """Each item: name, status (ok/missing), detail, url."""
    from .arch import native_arch, system32_dir
    if sys.platform != "win32":
        return []
    names = _uninstall_names()
    has_vc = _has_current_vc(names)
    vc_url = {"x64": VC_X64, "x86": VC_X86, "arm64": VC_ARM64}.get(
        native_arch(), VC_X64)
    sys32 = system32_dir()  # WOW64-safe (Sysnative from 32-bit processes)
    dx = os.path.isfile(os.path.join(sys32, "D3DCompiler_47.dll")) or \
        os.path.isfile(os.path.join(sys32, "d3d12.dll"))
    items = [{
        "name": "Visual C++ Redistributable 2015+ (v14)",
        "status": "ok" if has_vc else "missing",
        "detail": "required by most games and apps" if not has_vc else "installed",
        "url": vc_url,
    }]
    items.append({
        "name": "DirectX (D3DCompiler_47 / D3D12)",
        "status": "ok" if dx else "missing",
        "detail": "DirectX runtime components" if not dx else "present",
        "url": DX_SETUP,
    })
    return items
