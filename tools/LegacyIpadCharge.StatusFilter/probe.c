#include <windows.h>
#include <cfgmgr32.h>
#include <stdio.h>
#include "statusprobe.h"

int wmain(int argc, wchar_t **argv)
{
    const BOOL printProbe = argc == 2 && wcscmp(argv[1], L"--print-charge-probe") == 0;
    if (printProbe || (argc == 2 && wcscmp(argv[1], L"--print-charge-request") == 0)) {
        const unsigned char *setup = printProbe ? IpadChargeProbeSetup : IpadChargeSetup;
        for (size_t i = 0; i < sizeof(IpadChargeSetup); i++) {
            wprintf(L"%02X%c", (unsigned)setup[i], i + 1 == sizeof(IpadChargeSetup) ? L'\n' : L' ');
        }
        return 0;
    }
    const BOOL checkOnly = argc == 2 && wcscmp(argv[1], L"--check") == 0;
    const BOOL charge = argc == 2 && wcscmp(argv[1], L"--charge-once") == 0;
    const BOOL chargeProbe = argc == 2 && wcscmp(argv[1], L"--probe-charge-path") == 0;
    const BOOL vendorRequest = charge || chargeProbe;
    if (!checkOnly && !vendorRequest && (argc != 2 || wcscmp(argv[1], L"--run-once") != 0)) {
        fwprintf(stderr, L"Use --check, --run-once, --print-charge-probe, --print-charge-request, --probe-charge-path or --charge-once.\n");
        return 2;
    }
    if (vendorRequest) {
        WCHAR computer[MAX_COMPUTERNAME_LENGTH + 1];
        DWORD length = ARRAYSIZE(computer);
        if (!GetComputerNameW(computer, &length) || _wcsicmp(computer, L"IPAD-LAB") != 0) {
            fwprintf(stderr, L"Charging requests are enabled only in the IPAD-LAB guest.\n");
            return 2;
        }
    }

    ULONG length = 0;
    CONFIGRET result = CM_Get_Device_Interface_List_SizeW(
        &length, (LPGUID)&IpadStatusInterface, NULL, CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
    if (result != CR_SUCCESS || length < 3) {
        fwprintf(stderr, L"No active filter interface (CONFIGRET 0x%lX).\n", result);
        return 3;
    }
    PWSTR paths = HeapAlloc(GetProcessHeap(), 0, length * sizeof(wchar_t));
    if (paths == NULL) return 1;
    result = CM_Get_Device_Interface_ListW((LPGUID)&IpadStatusInterface, NULL, paths, length,
                                           CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
    if (result != CR_SUCCESS || wcslen(paths) + 2 != length) {
        fwprintf(stderr, L"Expected one active filter interface (CONFIGRET 0x%lX).\n", result);
        HeapFree(GetProcessHeap(), 0, paths);
        return 3;
    }

    HANDLE device = CreateFileW(paths, vendorRequest ? GENERIC_WRITE : GENERIC_READ,
                                FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    DWORD error = device == INVALID_HANDLE_VALUE ? GetLastError() : ERROR_SUCCESS;
    HeapFree(GetProcessHeap(), 0, paths);
    if (error != ERROR_SUCCESS) {
        fwprintf(stderr, L"CreateFileW failed (Win32 %lu).\n", error);
        return 1;
    }
    if (checkOnly) {
        CloseHandle(device);
        wprintf(L"Filter interface opened. No USB request sent.\n");
        return 0;
    }

    USHORT status = 0;
    DWORD received = 0;
    const DWORD expected = vendorRequest ? 0 : sizeof(status);
    DWORD code = IOCTL_IPAD_GET_STATUS;
    if (charge) code = IOCTL_IPAD_CHARGE_ONCE;
    if (chargeProbe) code = IOCTL_IPAD_CHARGE_PROBE;
    BOOL ok = DeviceIoControl(device, code, NULL, 0, vendorRequest ? NULL : &status, expected, &received, NULL);
    error = ok ? ERROR_SUCCESS : GetLastError();
    CloseHandle(device);
    if (!ok || received != expected) {
        fwprintf(stderr, L"%ls failed (Win32 %lu, %lu bytes).\n", vendorRequest ? L"Charge request" : L"GET_STATUS", error, received);
        return 1;
    }
    if (chargeProbe) {
        wprintf(L"Charge probe completed (0 mA extra in active and suspend states).\n");
    } else if (charge) {
        wprintf(L"Charge request completed. Measure VBUS and battery current to verify the effect.\n");
    } else {
        wprintf(L"GET_STATUS succeeded: 2 bytes, device status 0x%04X.\n", (unsigned)status);
    }
    return 0;
}
