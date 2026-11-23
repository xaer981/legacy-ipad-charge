# Legacy iPad USB Charging for Windows

<p align="center"><strong>Charge an older iPad while it stays connected to your Windows PC for USB data.</strong></p>

<p align="center">
  <a href="https://github.com/xaer981/legacy-ipad-charge/releases"><strong>Download for Windows</strong></a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#devices">Devices</a> ·
  <a href="#daily-use">Daily use</a> ·
  <a href="docs/README.md">Documentation</a>
</p>

<p align="center">
  <a href="#before-you-start"><img src="https://img.shields.io/badge/Windows-x64-35638c?style=flat-square" alt="Windows x64"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-61746c?style=flat-square" alt="MIT license"></a>
  <a href="https://boosty.to/xaer981"><img src="https://img.shields.io/badge/Boosty-support-c97932?style=flat-square" alt="Support on Boosty"></a>
</p>

<p align="center"><img src="docs/assets/battery-current.png" width="680" alt="Median battery current rose from −566 mA to +358 mA during a three-minute trial."></p>
<p align="center"><a href="#test-results">Battery current on an iPad 2 during a 2 A trial with v0.1.0</a></p>

## Before you start

- **Windows x64.** Use the ZIP from `v0.1.0` or later. It runs without installing .NET. The tested setup used Windows 11. [Windows 10 status](docs/libusb-charge.md#windows-10-x64-untested)
- **One iPad and a USB data cable.** Connect the iPad and make sure Windows recognizes it. [Check your iPad model](#devices) · [Check the port, cable, and adapter](docs/libusb-charge.md#check-the-usb-port)
- **Driver setup.** First-time setup needs administrator access and the separate [signed libusb-win32 package](docs/libusb-charge.md#install-the-device-filter). It lets the app send charging requests while Windows keeps the Apple driver. [Upgrade from the PowerShell release](docs/libusb-charge.md#upgrade-from-the-powershell-release)

## Quick start

### 1. Find your iPad

Extract `legacy-ipad-charge-windows-x64.zip`. Open its folder in File Explorer, type `powershell` in the address bar, and press Enter. Run this command and compare the USB ID with the [device list](docs/compatibility.md).

```powershell
.\legacy-ipad-charge.exe devices
```

### 2. Install and check the filter

Follow the [installation guide](docs/libusb-charge.md#install-the-device-filter). Keep the rollback file so you can remove the filter later. Reconnect the iPad to the same port and run this check. [Already installed the filter?](docs/libusb-charge.md#upgrade-from-the-powershell-release)

```powershell
.\legacy-ipad-charge.exe charge check
```

Continue when you see `PASS`. [If the check fails](docs/libusb-charge.md#troubleshooting)

### 3. Try 1 A for 30 seconds

First confirm that the **port, cable, and any adapter** each support **1 A at 5 V while carrying data**. Keep the standard USB mode if you can't confirm the rating. The app saves the number you enter. [How to check the rating](docs/libusb-charge.md#check-the-usb-port)

```powershell
.\legacy-ipad-charge.exe charge confirm-port --max-ma 1000
```

After `PORT_LIMIT_SAVED`, start the trial. It requests 1 A for 30 seconds, then requests the 500 mA baseline. Look for `RAISE_OK` and `RESTORE_OK`.

```powershell
.\legacy-ipad-charge.exe charge trial
```

Check that USB data stays connected and verify charging. [Standard iOS](docs/libusb-charge.md#stock-ios) · [Jailbroken iPad](docs/libusb-charge.md#jailbreak-and-ssh) · [Troubleshooting](docs/libusb-charge.md#troubleshooting)

## Devices

| iPad | Windows charging status |
| --- | --- |
| **iPad 2 GSM**<br>`A1396` · `05AC:12A2` | **Tested with `v0.1.0`.** Three-minute trials at 1 A and 2 A. USB storage was accessible during both, and the iPad returned to 500 mA. |
| **Other 30-pin iPads**<br>iPad (2010), iPad 2 Wi-Fi and CDMA, iPad (3rd generation) | **Experimental.** Hardware tests pending. |
| **Early Lightning iPads**<br>iPad (4th generation), early iPad mini and iPad Air | **Experimental.** Hardware tests pending. |

The tested setup used **Windows 11 x64** and **jailbroken iOS 9.3.5**. Hardware tests with [stock iOS](docs/libusb-charge.md#stock-ios) and [Windows 10 x64](docs/libusb-charge.md#windows-10-x64-untested) are pending. [Full device list and USB IDs](docs/compatibility.md)

## Daily use

Staying at **1 A**? After a successful 1 A trial, run `charge enable --limit-ma 1000`.

For the default **2 A**, confirm the port, cable, and any adapter all support 2 A at 5 V with data. Save the higher rating and run a new trial.

```powershell
.\legacy-ipad-charge.exe charge confirm-port --max-ma 2000
.\legacy-ipad-charge.exe charge trial --limit-ma 2000
```

**Enable charging** after verifying charging and USB data during the trial. This mode has no timer. Use `charge restore` to end it.

```powershell
.\legacy-ipad-charge.exe charge enable
```

For **1.5 A**, save a limit of 1,500 mA once confirmed, then pass `--limit-ma 1500` to the trial and enable commands. After reconnecting the iPad, run `charge check` before enabling charging again.

**Return to 500 mA** with `charge restore`. This works without a saved port rating.

```powershell
.\legacy-ipad-charge.exe charge restore
```

[All charging commands](docs/libusb-charge.md#charging-commands) · [Remove the filter](docs/libusb-charge.md#remove-the-device-filter)

## Test results

At the **2 A** reported input limit, median battery current went from **−566 mA to +358 mA** during a three-minute trial. The **1 A** trial had a median of **−49 mA**. Both trials returned to **500 mA** and passed every USB storage check. The figures are from the iPad's battery telemetry. [Test setup and measurements](docs/compatibility.md#tested-configuration)

## Help

| I want to… | Where to go |
| --- | --- |
| Fix a setup or charging error | [Troubleshooting](docs/libusb-charge.md#troubleshooting) |
| Check USB detection or collect details about the driver | [Read-only USB diagnostics](docs/diagnostics.md#inspect-an-ipad) |
| Check charging on stock iOS | [Charging indicator and USB meter](docs/libusb-charge.md#stock-ios) |
| Measure battery current on a jailbroken iPad | [SSH and ioreg](docs/libusb-charge.md#jailbreak-and-ssh) |
| Share results for my iPad | [Compatibility report](docs/compatibility.md#report-your-ipad) |

To share a read-only USB report, run this from the extracted ZIP folder and review the JSON before posting.

```powershell
.\legacy-ipad-charge.exe report --format json > usb-report.json
```

## Build and test

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build from source. The tests run without an iPad. [Build troubleshooting](docs/diagnostics.md#build-troubleshooting)

```powershell
dotnet build LegacyIpadCharge.slnx '-p:Platform=Any CPU' -warnaserror
dotnet run --no-build --project tests/LegacyIpadCharge.Tests -p:Platform=AnyCPU
.\tools\Build-Release.ps1
```

The build writes the Windows ZIP and `SHA256SUMS.txt` to `bin/release`.

## Support the project

[Support development on Boosty](https://boosty.to/xaer981) or help by sharing a [compatibility report](docs/compatibility.md#report-your-ipad).

## License

[MIT License](LICENSE) · [References and third-party notices](docs/references.md)

<p align="center"><a href="https://github.com/xaer981"><img src="https://raw.githubusercontent.com/xaer981/xaer981/0aa64a3f8982d7dd0f90d5eaf2254ccc3d614994/main_cat.gif" alt="Animated cat" width="128" height="40"></a></p>
