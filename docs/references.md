# References and notices

[All guides](README.md) · [Quick start](../README.md#quick-start)

## Apple USB charging

- [`mkorenkov/ipad_charge`](https://github.com/mkorenkov/ipad_charge/blob/master/ipad_charge.c) and [`FD-/usb_charge`](https://github.com/FD-/usb_charge/blob/master/usb_charge.c) document the Apple USB request and iPad product IDs on Linux. The Windows CLI uses those IDs to identify candidates. The request layout and Windows driver integration were implemented separately.
- The Linux tools send a different `wValue` from the one tested here. This project keeps the observed Windows request layout and lists Windows evidence by model in the [compatibility guide](compatibility.md#tested-configuration).
- [Apple accessory documentation](https://developer.apple.com/accessories/) and the [Linux Apple MFi fast-charge driver](https://github.com/torvalds/linux/blob/master/drivers/usb/misc/apple-mfi-fastcharge.c) provide protocol context.

## Windows USB stack

- [Microsoft USB control transfers](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-control-transfer) and [WinUSB considerations](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/winusb-considerations).
- [USB composite device configuration](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/selecting-the-configuration-for-a-multiple-interface--composite--usb-d) and [driver package catalogs](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/catalog-files).
- [WinUSB initialization](https://learn.microsoft.com/en-us/windows/win32/api/winusb/nf-winusb-winusb_initialize), [Configuration Manager](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/) and [USBView](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/usbview).

## Licenses

- The project's source and documentation use the [MIT License](../LICENSE).
- Download [libusb-win32 1.4.0.2](https://github.com/mcuee/libusb-win32/releases/tag/release_1.4.0.2) separately under its own license. The Windows ZIP includes the project's charging and diagnostic executable.
- The Windows ZIP includes the license and third-party notices for the .NET runtime version used to build the executable.
