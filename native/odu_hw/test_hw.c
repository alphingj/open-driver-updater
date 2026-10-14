/* odu_hw self-test. Returns 0 on success. */
#include "odu_hw.h"
#include <stdio.h>
#include <string.h>

static int fails = 0;
#define CHECK(cond) do { if (!(cond)) { printf("FAIL line %d: %s\n", __LINE__, #cond); fails++; } } while (0)

int main(void) {
    char out[32];
    odu_canonical_arch("AMD64", out);      CHECK(!strcmp(out, "x64"));
    odu_canonical_arch("x86_64", out);    CHECK(!strcmp(out, "x64"));
    odu_canonical_arch("ARM64", out);     CHECK(!strcmp(out, "arm64"));
    odu_canonical_arch("aarch64", out);   CHECK(!strcmp(out, "arm64"));
    odu_canonical_arch("x86", out);       CHECK(!strcmp(out, "x86"));
    odu_canonical_arch("weird", out);     CHECK(!strcmp(out, "x86"));

    odu_hwid_vendor("PCI\\VEN_1022&DEV_15E2", out); CHECK(!strcmp(out, "VEN_1022"));
    odu_hwid_vendor("USB\\VID_0E8D&PID_2005", out); CHECK(!strcmp(out, "VEN_0E8D"));
    odu_hwid_vendor("ACPI\\PNP0200", out);          CHECK(!strcmp(out, ""));

    CHECK(odu_hwid_ven_dev("ACP\\DEVTYPE_0001&DEVREV_0000&VEN_1022&DEV_15E2&SUBSYS_10431603", out)
          && !strcmp(out, "VEN_1022&DEV_15E2"));
    CHECK(odu_hwid_ven_dev("USB\\VID_0E8D&PID_2005&REV_0223", out)
          && !strcmp(out, "VID_0E8D&PID_2005"));
    CHECK(!odu_hwid_ven_dev("ACPI\\VEN_PNP&DEV_0200", out));

    CHECK(odu_arch_tag_ok("", "x64"));
    CHECK(odu_arch_tag_ok(".NTAMD64", "x64"));
    CHECK(!odu_arch_tag_ok(".NTAMD64", "arm64")); /* substring trap */
    CHECK(!odu_arch_tag_ok(".NTX86", "x64"));
    CHECK(odu_arch_tag_ok(".NTARM64", "arm64"));
    if (!fails) printf("HW SELFTEST OK\n");
    return fails != 0;
}
