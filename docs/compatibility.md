# iPad compatibility

The charging script currently targets the iPad 2 GSM (`05AC:12A2`). Owners of any candidate model can [report their iPad](#report-your-ipad) using read-only diagnostics with the stock Apple driver.

Charging was tested on Windows 11 x64. [Windows 10 x64 is untested](libusb-charge.md#windows-10-x64-untested).

| iPad | Model numbers | Connector | Windows charging trial |
| --- | --- | --- | --- |
| iPad (2010) | `A1219`, `A1337` | 30-pin | Awaiting a report |
| iPad 2 Wi-Fi and CDMA | `A1395`, `A1397` | 30-pin | Awaiting a report |
| iPad 2 GSM | `A1396` | 30-pin | [2,000 mA request tested for 30 minutes](#tested-configuration) |
| iPad (3rd generation) | `A1416`, `A1430`, `A1403` | 30-pin | Awaiting a report |
| iPad (4th generation) | `A1458`, `A1459`, `A1460` | Lightning | Awaiting a report |
| Early iPad mini and iPad Air | [Look up your exact model](https://support.apple.com/en-us/108043) | Lightning | Awaiting a report |

Apple's [model guide](https://support.apple.com/en-us/108043) shows where to find the model number on the back. A USB PID can be shared, and the Wi-Fi iPad 2 has multiple revisions under `A1395`. Include `ProductType` if available, such as `iPad2,4`. The candidate list draws on the [Linux `usb_charge` implementation](https://github.com/FD-/usb_charge/blob/master/usb_charge.c#L10-L21).

## Tested configuration

| Component | Tested setup |
| --- | --- |
| iPad | iPad 2 GSM, `A1396`, `iPad2,2`, USB `05AC:12A2`, iOS 9.3.5 |
| iOS setup | Jailbreak and SSH access for `ioreg` telemetry. A charging trial on stock iOS is pending. |
| Windows | Windows 11 x64, Secure Boot enabled. Memory Integrity (HVCI) was inactive when checked. |
| Drivers | Apple USB `538.0.0.0` with `libusb-win32 1.4.0.2 x64` as a per-device filter |
| USB path | Rear Thunderbolt 4, USB-C to USB-A adapter, 30-pin USB data cable |
| Workload | iPad acting as an active USB second display |

The README figure uses a three-minute trial at a reported 1,500 mA adapter limit. Median battery current went from −647 mA to +201 mA. The iPad reported 500 mA after the timed restore, and the USB data session stayed active. [Lab measurements](feasibility.md#три-минуты-при-15-а-со-скриптом-из-репозитория)

The same iPad accepted both limits in a later pair of three-minute trials.

| Limit reported by iPad | Median battery current | Limit after restore |
| ---: | ---: | ---: |
| 1,500 mA | +207 mA | 500 mA |
| 2,000 mA | +212 mA | 500 mA |

The trials used iOS telemetry. [Comparison measurements](feasibility.md#три-минуты-при-15-и-20-а)

In a subsequent 30-minute trial on the same USB path, all 298 successful samples showed the 2,000 mA limit. Median battery current was +210 mA, and all six post-restore samples reported 500 mA. The trial measured battery current through `ioreg`. The USB bus current and the USB-C to USB-A adapter's rating remain unverified. Check your own port rating before using this limit. [30-minute trial](feasibility.md#30-минут-при-20-а) · [Check the USB port](libusb-charge.md#check-the-usb-port)

Controlled charging trials on stock iOS and without an active USB data workload are pending.

## Filter installation and rollback

On 23 November 2026, the installer and rollback scripts from the release ZIP were tested with the same A1396 and Apple USB driver on Windows 11 x64 build 26300. Secure Boot was enabled and Memory Integrity was inactive. The previous filter was fully removed before testing the installer.

After reconnecting the iPad, `-CheckDevice` confirmed that the filter was active. File Explorer could enumerate the iPad's internal storage with the filter loaded and after rollback. Rollback removed the libusb-win32 package, service, driver file, and installation journal while preserving the Apple USB driver.

A separate test on the same PC ran `-Enable -LimitMa 1000` followed by a manual `-Restore` from the release ZIP. The iPad reported 500 → 1,000 → 500 mA in `ioreg`, and Windows could enumerate the iPad's internal storage at each step. [Manual restore test](feasibility.md#ручной-возврат-после-enable)

Testing on a fresh Windows installation, with Memory Integrity enabled, and with manual interruption of an active timed `-RunOnce` trial is pending.

## Report your iPad

1. Run the [read-only quick start](../README.md#quick-start) and open `usb-report.json` to review the output. If the CLI cannot run, include its error and whether Windows recognized the iPad.
2. On GitHub, select **Issues → New issue → iPad compatibility report**. Add your model, iOS version, whether the iPad has a jailbreak, Windows version, and the port, cable, and any adapter you used. Paste the reviewed USB report with personal identifiers removed from any additional logs.
3. Describe what you saw. A report from the stock Apple stack helps identify the device and plan a test. If you completed a charging trial, include the requested limit and duration, iPad workload, charging observations before, during, and after, the result of the restore, and whether USB data stayed connected.

For a charging report, identify the source of your evidence. The Windows log shows transfer outcomes. The iPad's charge icon and battery percentage describe observed behavior. A USB meter measures input current. `ioreg` provides the iOS-reported adapter limit and battery current. Reports with only transfer outcomes are useful for USB compatibility. Confirming a change in charging requires measurements or observed charging behavior.
