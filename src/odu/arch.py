"""Architecture detection: x86 / x64 / ARM64, INF arch tags, WOW64-safe paths."""
from __future__ import annotations
import os
import platform
import struct


def machine() -> str:
    return (platform.machine() or "").lower()


def canonical_arch(m: str | None = None) -> str:
    m = (m or machine()).lower().replace("_", "-")
    if m in ("amd64", "x86-64", "x86-64", "x64", "em64t"):
        return "x64"
    if m in ("arm64", "aarch64", "arm64e"):
        return "arm64"
    if m in ("x86", "i386", "i486", "i586", "i686", "x86-32"):
        return "x86"
    # struct pointer size fallback
    if struct.calcsize("P") * 8 == 64:
        return "x64"
    return "x86"


def inf_arch_tags(arch: str) -> tuple[str, ...]:
    """INF Manufacturer decorations valid for this arch (most-specific first)."""
    arch = canonical_arch(arch)
    if arch == "x64":
        return ("NTAMD64", "NTX86", "NT")
    if arch == "arm64":
        return ("NTARM64", "NTAMD64", "NT")
    return ("NTX86", "NT")


def is_arch_compatible(inf_models_section: str, arch: str) -> bool:
    sec = inf_models_section.upper()
    for tag in inf_arch_tags(arch):
        if f".{tag}" in sec or f"[{tag}" in sec or sec.strip().endswith(tag):
            return True
    # Undecorated [Manufacturer] with plain models section matches everything.
    if "NT" not in sec:
        return True
    return False


def system32_dir() -> str:
    """WOW64-safe System32 (uses Sysnative from 32-bit Python on 64-bit Windows)."""
    windir = os.environ.get("SystemRoot", r"C:\Windows")
    sys32 = os.path.join(windir, "System32")
    if canonical_arch() == "x86" and os.environ.get("PROCESSOR_ARCHITEW6432"):
        native = os.path.join(windir, "Sysnative")
        if os.path.isdir(native):
            return native
    return sys32


def pnputil_path() -> str:
    p = os.path.join(system32_dir(), "pnputil.exe")
    return p if os.path.exists(p) else "pnputil.exe"
