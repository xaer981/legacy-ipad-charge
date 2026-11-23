#pragma once

#define IOCTL_IPAD_GET_STATUS CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_READ_ACCESS)
#define IOCTL_IPAD_CHARGE_ONCE CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_WRITE_ACCESS)
#define IOCTL_IPAD_CHARGE_PROBE CTL_CODE(FILE_DEVICE_UNKNOWN, 0x802, METHOD_BUFFERED, FILE_WRITE_ACCESS)

// Apple MFi R9 table 2-8: no extra suspend current, 500 mA extra when active.
static const unsigned char IpadChargeSetup[8] = {0x40, 0x40, 0, 0, 0xf4, 0x01, 0, 0};
static const unsigned char IpadChargeProbeSetup[8] = {0x40, 0x40, 0, 0, 0, 0, 0, 0};

static const GUID IpadStatusInterface =
    {0x75c42e4c, 0x53a6, 0x4d33, {0x83, 0xa0, 0xf2, 0xac, 0x8a, 0xc7, 0x24, 0x6d}};
