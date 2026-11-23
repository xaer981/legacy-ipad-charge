# Legacy iPad USB Charging for Windows

**Charge an older iPad over USB and keep the data connection to your PC.**

[Quick start](#quick-start) · [Devices](docs/compatibility.md) · [Daily use](#daily-use) · [Documentation](docs/README.md)

## Before you start

- **Windows x64.** The app runs without installing .NET. The tested setup used Windows 11. [Windows 10 status](docs/libusb-charge.md#windows-10-x64-untested)
- **One iPad and a USB data cable.** Connect the iPad and make sure Windows recognizes it. [Check your iPad model](docs/compatibility.md) · [Check the port, cable, and adapter](docs/libusb-charge.md#check-the-usb-port)
- **Driver setup.** First-time setup needs administrator access and the separate [signed libusb-win32 package](docs/libusb-charge.md#install-the-device-filter). It lets the app send charging requests while Windows keeps the Apple driver. [Upgrade from the PowerShell release](docs/libusb-charge.md#upgrade-from-the-powershell-release)

## Quick start

### 1. Find your iPad

Open the extracted ZIP folder in File Explorer. Type `powershell` in the address bar and press Enter. Run this command and compare the USB ID with the [device list](docs/compatibility.md).

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

## Daily use

Staying at **1 A**? After a successful 1 A trial, run `charge enable --limit-ma 1000`.

For the default **2 A**, confirm the port, cable, and any adapter all support 2 A at 5 V with data. Save the higher rating and run a new trial.

```powershell
.\legacy-ipad-charge.exe charge confirm-port --max-ma 2000
.\legacy-ipad-charge.exe charge trial --limit-ma 2000
```

Enable charging after verifying charging and USB data during the trial. This mode has no timer. Use `charge restore` to end it.

```powershell
.\legacy-ipad-charge.exe charge enable
```

For **1.5 A**, save a limit of 1,500 mA once confirmed, then pass `--limit-ma 1500` to the trial and enable commands. After reconnecting the iPad, run `charge check` before enabling charging again.

Run this command to return to 500 mA.

```powershell
.\legacy-ipad-charge.exe charge restore
```

When removing an active filter, unplug and reconnect the iPad when prompted, then run the same rollback command again. [Remove the filter](docs/libusb-charge.md#remove-the-device-filter)

## Help

To share a USB report, run this command and review the JSON before posting. [Compatibility report](docs/compatibility.md#report-your-ipad)

```powershell
.\legacy-ipad-charge.exe report --format json > usb-report.json
```

[Troubleshooting](docs/libusb-charge.md#troubleshooting) · [All guides](docs/README.md)

The ZIP includes `dotnet-THIRD-PARTY-NOTICES.txt`. [Project license](LICENSE)
