// odu_scan - instant device enumeration via SetupAPI (no PowerShell).
// Emits a JSON array: instance_id, friendly_name, dev_class, manufacturer,
// status (OK/ERROR/DISCONNECTED), problem_code, hwid[], ven_dev,
// driver_version, driver_date, driver_provider.
#define WIN32_LEAN_AND_MEAN
#define INITGUID
#include <windows.h>
#include <setupapi.h>
#include <cfgmgr32.h>
#include <devpropdef.h>
#include <devpkey.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "odu_hw.h"

#pragma comment(lib, "SetupAPI.Lib")
#pragma comment(lib, "Cfgmgr32.Lib")

static void json_escape(FILE *f, const char *s) {
    for (; *s; s++) {
        unsigned char c = (unsigned char)*s;
        switch (c) {
        case '"': fputs("\\\"", f); break;
        case '\\': fputs("\\\\", f); break;
        case '\b': fputs("\\b", f); break;
        case '\f': fputs("\\f", f); break;
        case '\n': fputs("\\n", f); break;
        case '\r': fputs("\\r", f); break;
        case '\t': fputs("\\t", f); break;
        default:
            if (c < 0x20) fprintf(f, "\\u%04x", c);
            else fputc(c, f);
        }
    }
}

static void get_reg_str(HDEVINFO h, PSP_DEVINFO_DATA d, DWORD prop,
                        char *out, size_t cap) {
    DWORD type = 0, need = 0;
    out[0] = '\0';
    if (!SetupDiGetDeviceRegistryPropertyA(h, d, prop, &type,
                                           (PBYTE)out, (DWORD)cap, &need))
        out[0] = '\0';
    out[cap - 1] = '\0';
}

static void wstr_to_utf8(const WCHAR *w, char *out, size_t cap) {
    out[0] = '\0';
    if (!w) return;
    WideCharToMultiByte(CP_UTF8, 0, w, -1, out, (int)cap, NULL, NULL);
    out[cap - 1] = '\0';
}

static void read_driver_info(HDEVINFO h, PSP_DEVINFO_DATA d,
                              char *ver, char *date, char *prov) {
    DEVPROPTYPE pt = 0;
    DWORD sz;
    WCHAR wv[64] = L"", wp[128] = L"";
    ver[0] = date[0] = prov[0] = '\0';
    sz = sizeof(wv);
    if (SetupDiGetDevicePropertyW(h, d, &DEVPKEY_Device_DriverVersion, &pt,
                                  (PBYTE)wv, sz, &sz, 0))
        wstr_to_utf8(wv, ver, 64);
    {
        FILETIME ft;
        SYSTEMTIME st;
        sz = sizeof(ft);
        pt = 0;
        if (SetupDiGetDevicePropertyW(h, d, &DEVPKEY_Device_DriverDate, &pt,
                                      (PBYTE)&ft, sz, &sz, 0) &&
            FileTimeToSystemTime(&ft, &st))
            _snprintf_s(date, 16, _TRUNCATE, "%04d%02d%02d",
                        st.wYear, st.wMonth, st.wDay);
    }
    sz = sizeof(wp);
    if (SetupDiGetDevicePropertyW(h, d, &DEVPKEY_Device_DriverProvider, &pt,
                                  (PBYTE)wp, sz, &sz, 0))
        wstr_to_utf8(wp, prov, 128);
}

int main(void) {
    HDEVINFO h = SetupDiGetClassDevsA(NULL, NULL, NULL, DIGCF_ALLCLASSES);
    SP_DEVINFO_DATA dev;
    DWORD i;
    int first = 1;
    if (h == INVALID_HANDLE_VALUE) {
        printf("[]");
        return 1;
    }
    printf("[");
    dev.cbSize = sizeof(dev);
    for (i = 0; SetupDiEnumDeviceInfo(h, i, &dev); i++) {
        char inst[512] = "", friendly[512] = "", cls[128] = "",
             mfg[256] = "", desc[512] = "",
             ver[64] = "", date[16] = "", prov[128] = "";
        char hwids[8192] = "";
        DWORD type = 0, need = 0;
        DEVINST dn = 0;
        ULONG status = 0, problem = 0;
        char st[16] = "OK";
        int has_hw = 0;

        if (!SetupDiGetDeviceInstanceIdA(h, &dev, inst, sizeof(inst), NULL))
            continue;
        get_reg_str(h, &dev, SPDRP_FRIENDLYNAME, friendly, sizeof(friendly));
        get_reg_str(h, &dev, SPDRP_DEVICEDESC, desc, sizeof(desc));
        get_reg_str(h, &dev, SPDRP_CLASS, cls, sizeof(cls));
        get_reg_str(h, &dev, SPDRP_MFG, mfg, sizeof(mfg));
        if (SetupDiGetDeviceRegistryPropertyA(h, &dev, SPDRP_HARDWAREID,
                                              &type, (PBYTE)hwids,
                                              sizeof(hwids) - 2, &need))
            has_hw = 1;

        if (CM_Locate_DevNodeA(&dn, inst, CM_LOCATE_DEVNODE_PHANTOM) != CR_SUCCESS)
            dn = dev.DevInst;
        if (CM_Get_DevNode_Status(&status, &problem, dn, 0) == CR_SUCCESS) {
            if (problem == 45)
                strcpy_s(st, sizeof(st), "DISCONNECTED");
            else if (problem != 0 || (status & DN_HAS_PROBLEM))
                strcpy_s(st, sizeof(st), "ERROR");
        }
        if (!strcmp(st, "OK")) {
            /* Phantoms report CM_PROB_NONE: IsPresent is the true signal. */
            DEVPROPTYPE pt = 0;
            BYTE present = TRUE;
            DWORD psz = sizeof(present);
            if (SetupDiGetDevicePropertyW(h, &dev, &DEVPKEY_Device_IsPresent,
                                          &pt, &present, psz, &psz, 0) &&
                pt == DEVPROP_TYPE_BOOLEAN && !present) {
                strcpy_s(st, sizeof(st), "DISCONNECTED");
                problem = 45;
            }
        }
        read_driver_info(h, &dev, ver, date, prov);

        if (!first) printf(",");
        first = 0;
        printf("{\"instance_id\":\"");
        json_escape(stdout, inst);
        printf("\",\"friendly_name\":\"");
        json_escape(stdout, friendly[0] ? friendly : desc);
        printf("\",\"dev_class\":\"");
        json_escape(stdout, cls);
        printf("\",\"manufacturer\":\"");
        json_escape(stdout, mfg);
        printf("\",\"status\":\"%s\",\"problem_code\":%lu,\"hwid\":[", st, problem);
        if (has_hw) {
            const char *p = hwids;
            int n = 0;
            while (*p && n < 8) {
                char vd[32] = "";
                if (n) printf(",");
                printf("\"");
                json_escape(stdout, p);
                printf("\"");
                p += strlen(p) + 1;
                n++;
            }
        }
        {
            char vd[32] = "";
            const char *p = hwids;
            printf("],\"ven_dev\":\"");
            if (has_hw && *p && odu_hwid_ven_dev(p, vd))
                json_escape(stdout, vd);
            printf("\",\"driver_version\":\"");
            json_escape(stdout, ver);
            printf("\",\"driver_date\":\"");
            json_escape(stdout, date);
            printf("\",\"driver_provider\":\"");
            json_escape(stdout, prov);
            printf("\"}");
        }
    }
    printf("]");
    SetupDiDestroyDeviceInfoList(h);
    return 0;
}
