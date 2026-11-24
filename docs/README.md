# Documentation

[Overview](../README.md) · [Windows setup](libusb-charge.md) · [Linux setup](linux.md) · [Device compatibility](compatibility.md)

| I want to… | Where to go |
| --- | --- |
| Check my hardware | [USB port and cable](#check-the-usb-port) · [iPad model](compatibility.md) · [Windows 10 status](libusb-charge.md#windows-10-x64-untested) |
| Set up charging for the first time | [Windows quick start](../README.md#quick-start) · [Linux quick start](linux.md#1-get-the-cli) |
| Try a higher current or enable it for daily use | [Windows charging commands](libusb-charge.md#charging-commands) · [Linux trial and daily use](linux.md#3-try-a-higher-current) |
| Check whether charging worked | [Stock iOS or jailbreak](#verify-charging) |
| Fix a setup or charging error | [Windows](libusb-charge.md#troubleshooting) · [Linux](linux.md#troubleshooting) · [Read-only diagnostics](diagnostics.md) |
| Return to 500 mA | [Windows](libusb-charge.md#restore-500-ma) · [Linux](linux.md#4-enable-charging-for-daily-use) |
| Upgrade or remove the Windows filter | [Upgrade from PowerShell](libusb-charge.md#upgrade-from-the-powershell-release) · [Remove the filter](libusb-charge.md#remove-the-device-filter) |
| Share results for a model | [Report your iPad](compatibility.md#report-your-ipad) |

## Check the USB port

1. Confirm the current rating at 5 V for the exact port, data cable, and every adapter or hub while carrying USB data. Use the lowest rating in the chain. Recheck it after changing a component, even if the USB path reported by the CLI stays the same.
2. Budget **500 mA** for a USB 2.0 iPad on a standard USB-A data port. A documented **BC 1.2 charging downstream port** supports **1,500 mA** with data. The default **2,000 mA** request needs a path rated for 2 A at 5 V with data. [USB-IF port limits](https://compliance.usb.org/index.asp?Format=Standard&UpdateFile=Policies) · [USB-IF charging port tests](https://compliance.usb.org/index.asp?UpdateFile=Battery+Charging&tgt=post)
3. Use `devices` and `inspect --pid <PID>` to identify the USB path. Get its electrical rating from the hardware specifications. `bMaxPower` is the device's descriptor value, and `ioreg` reports the limit recognized by iOS. A USB meter with data passthrough can measure input current and voltage during a trial. [USB diagnostics](diagnostics.md#inspect-an-ipad)

Keep the standard USB mode if the lowest rating is unknown or below 1 A. `charge confirm-port` records the rating you enter for the current path. You need to verify that value yourself.

## Verify charging

- **Stock iOS.** Observe the charging icon and battery percentage under the same workload. Use a USB meter with data passthrough to measure input current. [Windows observations](libusb-charge.md#stock-ios) · [Linux observations](linux.md#verify-charging)
- **Jailbroken iPad with existing SSH access.** Use `monitor` to log the reported adapter limit and battery current. [Read battery current](diagnostics.md#read-battery-current)

## Implementation

See [protocol details](libusb-charge.md#protocol-details) and [references and notices](references.md) for implementation background.
