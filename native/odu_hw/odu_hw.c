/* odu_hw implementation. See odu_hw.h. No OS calls, no heap use. */
#include "odu_hw.h"
#include <ctype.h>
#include <string.h>

static int eq_nocase(const char *a, const char *b, size_t n) {
    size_t i;
    for (i = 0; i < n; i++) {
        unsigned char ca = (unsigned char)a[i], cb = (unsigned char)b[i];
        if (tolower(ca) != tolower(cb)) return 0;
        if (ca == '\0') return 1;
    }
    return 1;
}

void odu_canonical_arch(const char *machine, char *out) {
    char tmp[32];
    size_t i;
    for (i = 0; i < sizeof(tmp) - 1 && machine && machine[i]; i++) {
        tmp[i] = (char)tolower((unsigned char)machine[i]);
        if (tmp[i] == '_') tmp[i] = '-';
    }
    tmp[i] = '\0';
    if (!strcmp(tmp, "amd64") || !strcmp(tmp, "x86-64") ||
        !strcmp(tmp, "x64") || !strcmp(tmp, "em64t")) {
        strcpy(out, ODU_ARCH_X64);
    } else if (!strcmp(tmp, "arm64") || !strcmp(tmp, "aarch64") ||
               !strcmp(tmp, "arm64e")) {
        strcpy(out, ODU_ARCH_ARM64);
    } else {
        strcpy(out, ODU_ARCH_X86);
    }
}

/* Find "(VEN|VID)_HHHH" and write "VEN_HHHH" (upper hex) to out. */
void odu_hwid_vendor(const char *hwid, char *out) {
    const char *p;
    out[0] = '\0';
    if (!hwid) return;
    for (p = hwid; *p; p++) {
        if ((eq_nocase(p, "VEN_", 4) || eq_nocase(p, "VID_", 4)) &&
            isxdigit((unsigned char)p[4]) && isxdigit((unsigned char)p[5]) &&
            isxdigit((unsigned char)p[6]) && isxdigit((unsigned char)p[7])) {
            sprintf_s(out, 32, "VEN_%c%c%c%c",
                    (char)toupper((unsigned char)p[4]),
                    (char)toupper((unsigned char)p[5]),
                    (char)toupper((unsigned char)p[6]),
                    (char)toupper((unsigned char)p[7]));
            return;
        }
    }
}

/* Find "(VEN|VID)_HHHH&(DEV|PID)_HHHH" and write the normalized pair. */
int odu_hwid_ven_dev(const char *hwid, char *out) {
    const char *p;
    char kind2[4];
    if (!hwid) return 0;
    for (p = hwid; *p; p++) {
        if ((eq_nocase(p, "VEN_", 4) || eq_nocase(p, "VID_", 4)) &&
            isxdigit((unsigned char)p[4]) && isxdigit((unsigned char)p[5]) &&
            isxdigit((unsigned char)p[6]) && isxdigit((unsigned char)p[7]) &&
            p[8] == '&' &&
            (eq_nocase(p + 9, "DEV_", 4) || eq_nocase(p + 9, "PID_", 4)) &&
            isxdigit((unsigned char)p[13]) && isxdigit((unsigned char)p[14]) &&
            isxdigit((unsigned char)p[15]) && isxdigit((unsigned char)p[16])) {
            memcpy(kind2, p + 9, 3);
            kind2[3] = '\0';
            sprintf_s(out, 32, "%s_%c%c%c%c&%s_%c%c%c%c",
                    eq_nocase(p, "VID_", 4) ? "VID" : "VEN",
                    (char)toupper((unsigned char)p[4]),
                    (char)toupper((unsigned char)p[5]),
                    (char)toupper((unsigned char)p[6]),
                    (char)toupper((unsigned char)p[7]),
                    (eq_nocase(p + 9, "PID_", 4) ? "PID" : "DEV"),
                    (char)toupper((unsigned char)p[13]),
                    (char)toupper((unsigned char)p[14]),
                    (char)toupper((unsigned char)p[15]),
                    (char)toupper((unsigned char)p[16]));
            return 1;
        }
    }
    return 0;
}

/* Exact-token match: "NTAMD64" must not satisfy an ARM64-only check. */
int odu_arch_tag_ok(const char *decoration, const char *arch) {
    char d[16], a[8];
    size_t i;
    for (i = 0; i < sizeof(d) - 1 && decoration && decoration[i]; i++)
        d[i] = (char)toupper((unsigned char)decoration[i]);
    d[i] = '\0';
    /* Strip a leading dot: ".NTAMD64" -> "NTAMD64". */
    if (d[0] == '.') memmove(d, d + 1, strlen(d));
    if (d[0] == '\0') return 1; /* undecorated matches everything */
    odu_canonical_arch(arch, a);
    if (!strcmp(a, ODU_ARCH_X64))
        return !strcmp(d, "NTAMD64") || !strcmp(d, "NT");
    if (!strcmp(a, ODU_ARCH_ARM64))
        return !strcmp(d, "NTARM64") || !strcmp(d, "NT");
    return !strcmp(d, "NTX86") || !strcmp(d, "NT");
}
