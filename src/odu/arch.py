"""Architecture detection: x86 / x64 / ARM64, INF arch tags, WOW64-safe paths.

Important distinction: driver packages must match the *OS* architecture, not the
*process* architecture. A 32-bit Python on 64-bit Windows (WOW64) still needs
x64 drivers, so every driver-matching decision uses :func:`native_arch`.
"""
from __future__ import annotations
import os
import platform
import re
import struct


def machine() -> str:
    return (platform.machine() or "").lower()


def canonical_arch(m: str | None = None) -> str:
    """Map a machine string (or the current process) to x86/x64/arm64."""
    m = (m or machine()).lower().replace("_", "-")
    if m in ("amd64", "x86-64", "x64", "em64t"):
        return "x64"
    if m in ("arm64", "aarch64", "arm64e"):
        return "arm64"
    if m in ("x86", "i386", "i486", "i586", "i686", "x86-32"):
        return "x86"
    # Last-resort fallback from pointer size (process arch, not OS arch).
    if struct.calcsize("P") * 8 == 64:
        return "x64"
    return "x86"


def native_arch() -> str:
    """OS architecture, correct under WOW64.

    `PROCESSOR_ARCHITEW6432` is only defined inside a WOW64 (32-bit) process on
    a 64-bit OS, in which case its value (AMD64/ARM64) is the *OS* arch.
    """
    wow64 = os.environ.get("PROCESSOR_ARCHITEW6432")
    if wow64:
        return canonical_arch(wow64)
    return canonical_arch(os.environ.get("PROCESSOR_ARCHITECTURE") or machine())


def inf_arch_tags(arch: str) -> tuple[str, ...]:
    """INF Manufacturer decorations installable on this arch.

    Only same-arch and undecorated (`NT`) sections qualify: an NTX86-only
    package will not install on x64, and NTAMD64 will not install on ARM64.
    """
    arch = canonical_arch(arch)
    if arch == "x64":
        return ("NTAMD64", "NT")
    if arch == "arm64":
        return ("NTARM64", "NT")
    return ("NTX86", "NT")


def _decorations(manufacturer_section: str) -> set[str]:
    """Arch decorations from a `[Manufacturer...]` header (exact tokens)."""
    sec = manufacturer_section.strip().upper().rstrip("]")
    if "." not in sec:
        return set()
    return {sec.rsplit(".", 1)[-1]}


def is_arch_compatible(manufacturer_section: str, arch: str) -> bool:
    """Check one `[Manufacturer...]` section header against an arch.

    Undecorated `[Manufacturer]` (no `.` suffix) matches every arch.
    Token-exact: NTAMD64 must not satisfy an NTARM64-only check.
    """
    decos = _decorations(manufacturer_section)
    if not decos:
        return True
    return bool(decos & set(inf_arch_tags(arch)))


def inf_arch_decorations(inf_text: str) -> set[str]:
    """All arch decorations in an INF: header suffixes plus `,NTxxx` model entries.

    Real INFs write either `[Manufacturer.NTAMD64]` or plain `[Manufacturer]`
    with `%Mfg%=Models,NTAMD64` entries; both forms are honored.
    """
    decos: set[str] = set()
    for m in re.findall(r"\[Manufacturer([^\]]*)\]", inf_text, re.I):
        decos |= _decorations("[" + m + "]")
    for block in re.findall(r"\[Manufacturer[^\]]*\](.*?)(?=^\[|\Z)",
                            inf_text, re.I | re.M | re.S):
        decos |= {t.upper() for t in re.findall(r",\s*(NT[A-Z0-9]+)", block, re.I)}
    return decos


def is_inf_compatible(inf_text: str, arch: str) -> bool:
    """INF-wide check: no decorations at all, or at least one matching arch."""
    decos = inf_arch_decorations(inf_text)
    if not decos:
        return True
    return bool(decos & set(inf_arch_tags(arch)))


def system32_dir() -> str:
    """WOW64-safe System32 (uses Sysnative from 32-bit Python on 64-bit Windows)."""
    windir = os.environ.get("SystemRoot", r"C:\Windows")
    sys32 = os.path.join(windir, "System32")
    if canonical_arch() == "x86" and os.environ.get("PROCESSOR_ARCHITEW6432"):
        native = os.path.join(windir, "Sysnative")
        if os.path.isdir(native):
            return native
    return sys32


def _system_tool(name: str) -> str:
    p = os.path.join(system32_dir(), name)
    return p if os.path.exists(p) else name


def pnputil_path() -> str:
    return _system_tool("pnputil.exe")


def expand_path() -> str:
    return _system_tool("expand.exe")
