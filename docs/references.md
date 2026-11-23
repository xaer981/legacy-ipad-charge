# Источники

## Apple USB и Linux

- [`mkorenkov/ipad_charge`, отправка и захват интерфейса](https://github.com/mkorenkov/ipad_charge/blob/master/ipad_charge.c) и [лицензия](https://github.com/mkorenkov/ipad_charge/blob/master/COPYING).
- [`FD-/usb_charge`, отправка и список VID/PID](https://github.com/FD-/usb_charge/blob/master/usb_charge.c), [описание пакета](https://github.com/FD-/usb_charge#how-it-works) и [лицензия](https://github.com/FD-/usb_charge/blob/master/COPYING). Оба Linux-проекта использованы как источники сведений о поведении протокола. Код CLI написан отдельно.
- [Linux `apple-mfi-fastcharge`](https://github.com/torvalds/linux/blob/master/drivers/usb/misc/apple-mfi-fastcharge.c).
- [Apple Accessories](https://developer.apple.com/accessories/) и [копия редакции Accessory Design Guidelines с разделом USB Power Capability Vendor Request](https://device.report/m/43c52c90006a7915765a487127eb3f5d0f040f03b4c6d220d8e17ae2a4252804.pdf). Применимость к iPad 2 проверяется отдельно.
- [Клиент подключения к usbmuxd и платформенные зависимости](https://github.com/libimobiledevice/libusbmuxd).

## Windows USB и PnP

- [Microsoft — USB control transfer](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-control-transfer), [WinUSB API](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/using-winusb-api-to-communicate-with-a-usb-device), [Initialize](https://learn.microsoft.com/en-us/windows/win32/api/winusb/nf-winusb-winusb_initialize) и [ControlTransfer](https://learn.microsoft.com/en-us/windows/win32/api/winusb/nf-winusb-winusb_controltransfer).
- [WinUSB, UMDF и KMDF](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/winusb-considerations), [USB generic parent и конфигурации](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/selecting-the-configuration-for-a-multiple-interface--composite--usb-d), [ограничения Windows.Devices.Usb](https://learn.microsoft.com/en-us/uwp/api/windows.devices.usb).
- [Configuration Manager](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/), [интерфейсы устройства](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_device_interface_listw) и [USBView](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/usbview).
- [Hub `EX` IOCTL](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/usbioctl/ni-usbioctl-ioctl_usb_get_node_connection_information_ex), [Hub `EX_V2` IOCTL](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/usbioctl/ni-usbioctl-ioctl_usb_get_node_connection_information_ex_v2) и [USB_DESCRIPTOR_REQUEST](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/usbioctl/ns-usbioctl-_usb_descriptor_request). Нативные константы и layout сверены с `cfgmgr32.h`, `devpkey.h`, `usbioctl.h` и `usbiodef.h` из Windows SDK `10.0.22621.0`.
- [libusb под Windows](https://github.com/libusb/libusb/wiki/Windows) и [windows_winusb.c](https://github.com/libusb/libusb/blob/master/libusb/os/windows_winusb.c).

## Лицензия проекта и уведомления

- Исходники проекта и его документация распространяются на условиях [MIT](../LICENSE).
- Подписанный [libusb-win32 1.4.0.2](https://github.com/mcuee/libusb-win32/releases/tag/release_1.4.0.2) загружается у авторов отдельно на условиях оригинального пакета. Windows ZIP содержит наши скрипты, лицензии и CLI.
- Встроенный .NET runtime сопровождают `dotnet-LICENSE.txt` и `dotnet-THIRD-PARTY-NOTICES.txt` из пакета той же версии, которую использовала сборка ZIP.
