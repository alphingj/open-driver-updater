/* odu_hw - shared C library: Hardware-ID utilities and INF arch matching.
 *
 * Pure C (C99), no OS calls: safe to link into the C++ scanner and to mirror
 * in the C# and Python layers. All functions are case-insensitive.
 */
#ifndef ODU_HW_H
#define ODU_HW_H

#ifdef __cplusplus
extern "C" {
#endif

/* Canonical OS arch tags returned in out (always NUL-terminated). */
#define ODU_ARCH_X86   "x86"
#define ODU_ARCH_X64   "x64"
#define ODU_ARCH_ARM64 "arm64"

/* Map a machine string ("AMD64", "ARM64", "x86", ...) to x86/x64/arm64.
 * out must hold at least 8 bytes. Unknown -> "x86". */
void odu_canonical_arch(const char *machine, char *out);

/* Extract "VEN_xxxx" (or VID_xxxx, normalized to VEN_) from a HWID.
 * out must hold at least 9 bytes. Empty string when absent. */
void odu_hwid_vendor(const char *hwid, char *out);

/* Extract the "VEN_xxxx&DEV_xxxx" (or VID/PID) pair buried in a HWID.
 * Returns 1 and fills out ( >= 22 bytes) on success, else 0. */
int odu_hwid_ven_dev(const char *hwid, char *out);

/* 1 when an INF Manufacturer decoration ("" , ".NTAMD64", ...) is installable
 * on arch ("x86"/"x64"/"arm64"). "" (undecorated) matches everything. */
int odu_arch_tag_ok(const char *decoration, const char *arch);

#ifdef __cplusplus
}
#endif

#endif
