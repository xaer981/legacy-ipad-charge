# iPad compatibility

[All guides](README.md) · [Quick start](../README.md#quick-start)

The CLI automatically selects one connected iPad from the table below. Disconnect other listed iPads before installing or charging. For experimental IDs, start with a timed 1,000 mA trial and verify charging and USB data before trying a higher current. If an ID is missing from the table, use the read-only CLI to [report your iPad](#report-your-ipad).

The Windows `v0.1.0` executable was tested on iPad 2 GSM with Windows 11 x64. A separate probe verified the Apple charging request on the same model in Kali WSL. Native Linux verification of the packaged CLI is pending. [Windows 10 x64 status](libusb-charge.md#windows-10-x64-untested) · [Linux results](#linux)

| Example iPad models | USB ID | Connector | Charging status on Windows |
| --- | --- | --- | --- |
| iPad (2010) | `05AC:129A` | 30-pin | Experimental |
| iPad 2 Wi-Fi | `05AC:129F` | 30-pin | Experimental |
| iPad 2 GSM | `05AC:12A2` | 30-pin | [Three-minute trials at 1 A and 2 A with `v0.1.0`](#tested-configuration) |
| iPad 2 CDMA | `05AC:12A3` | 30-pin | Experimental |
| iPad (3rd generation) | `05AC:12A4`, `05AC:12A5`, `05AC:12A6` | 30-pin | Experimental |
| iPad 2 Wi-Fi (rev. A), early iPad mini | `05AC:12A9` | 30-pin or Lightning | Experimental |
| iPad (4th generation), early iPad Air and iPad mini | `05AC:12AB` | Lightning | Experimental |

The model names and candidate IDs come from the [Linux implementations](references.md#apple-usb-charging). A USB ID can be shared across models. Use `devices` to read the connected iPad's ID and [Apple's model guide](https://support.apple.com/en-us/108043) to look up its A-number.

## Tested configuration

| Component | Tested setup |
| --- | --- |
| iPad | iPad 2 GSM, `A1396`, `iPad2,2`, USB `05AC:12A2`, iOS 9.3.5 |
| iOS setup | Jailbreak and SSH access for `ioreg` telemetry. A charging trial on stock iOS is pending. |
| Windows | Windows 11 x64 build 26300, Secure Boot enabled. Memory Integrity (HVCI) was inactive when checked. |
| Drivers | Apple USB `538.0.0.0` with `libusb-win32 1.4.0.2 x64` as a per-device filter |
| USB path | The tester confirmed at least 2 A at 5 V with data for the connection used in the `v0.1.0` trials. |
| Data check | Windows opened the iPad's `Internal Storage` during the trials and after rollback. |

On 23 November 2026, the `v0.1.0` executable ran both trials for 180 seconds. Each trial sent the 500 mA restore and `ioreg` confirmed the baseline afterward.

| Limit during trial | Successful `ioreg` samples at that limit | Median battery current before → during → after | Storage checks during trial |
| ---: | ---: | ---: | ---: |
| 1,000 mA | 34 | −1,065 → −49 → −567 mA | 12 of 12 |
| 2,000 mA | 36 | −566 → +358 → −619 mA | 12 of 12 |

The 1 A trial skipped two samples after SSH read errors. The README figure shows the 2 A trial. Input limits and battery current come from `ioreg`. USB bus current was not measured. Hands-on tests covered `charge trial`. The separate `charge enable` command is covered by automated tests. [Check the USB port](libusb-charge.md#check-the-usb-port)

### Earlier PowerShell results

The first public release used PowerShell for charging tests. On the same iPad, a three-minute 1.5 A trial showed −647 → +201 mA median battery current. Later three-minute trials showed +207 mA at 1.5 A and +212 mA at 2 A. A 30-minute trial reported 2 A across 298 successful samples, with a median of +210 mA and six samples at 500 mA after restore. These tests used a rear Thunderbolt 4 port, a USB-C to USB-A adapter, and a 30-pin data cable. The adapter's rating for those earlier tests was unverified.

## Filter installation and rollback

The `v0.1.0` ZIP was tested from the stock Apple stack. `filter install` created a rollback file. After reconnecting the iPad, `charge check` reported `PASS`.

`filter remove` detached the filter. After reconnecting, a second invocation removed the package, service, driver file, and installation journal. Windows kept Apple USB 538.0.0.0, File Explorer opened the iPad's storage, and `ioreg` reported 500 mA.

Earlier PowerShell tests also covered filter installation and rollback. A separate run verified 500 → 1,000 → 500 mA in `ioreg` with a manual restore.

## Linux

An independent libusb probe tested the iPad 2 GSM (`A1396`, `05AC:12A2`, iOS 9.3.5 with jailbreak) through USB/IP on Kali WSL 2. The probe used the physical USB path rated for 2 A at 5 V with data. Each trial ran for 30 seconds.

| Requested limit | Samples at the requested limit | Last observed battery current during the trial | USB data checks |
| --- | --- | --- | --- |
| 1,000 mA | 15 of 15 | +88 mA | 6 of 6 |
| 2,000 mA | 15 of 15 | +440 mA | 6 of 6 |

Both trials returned to 500 mA in `ioreg`. The figures describe the limit reported by iOS and the battery current. Hardware verification of the packaged CLI on native Linux is pending. See the [Linux guide](linux.md) for scope and setup.

## Report your iPad

1. Create and review a read-only USB report ([Windows](../README.md#help) · [Linux](linux.md#share-a-compatibility-report)). If the CLI cannot run, include its error and whether the host recognized the iPad.
2. On GitHub, select **Issues → New issue → iPad compatibility report**. Include the release tag, the CLI version or the source commit. Add your model, iOS version, whether the iPad has a jailbreak, host OS version, and the port, cable, and any adapter you used. Paste the reviewed USB report with personal identifiers removed from any additional logs.
3. Describe what you saw. A read-only report from Windows or Linux helps identify the device and plan a test. If you completed a charging trial, include the requested limit and duration, iPad workload, charging observations before, during, and after, the result of the restore, and whether USB data stayed connected.

For a charging report, identify the source of your evidence. The CLI output shows transfer outcomes. The iPad's charge icon and battery percentage describe observed behavior. A USB meter measures input current. `ioreg` provides the iOS-reported adapter limit and battery current. Reports with only transfer outcomes are useful for USB compatibility. Confirming a change in charging requires measurements or observed charging behavior.
