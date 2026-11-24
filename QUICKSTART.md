# Legacy iPad USB Charging for Windows

**Charge an older iPad over USB and keep the data connection to your PC.**

[Quick start](#quick-start) · [Devices](docs/compatibility.md) · [Daily use](#daily-use) · [Documentation](docs/README.md)

## Before you start

Use one [listed iPad](docs/compatibility.md) and a USB data cable. The .NET runtime is included. Tested on Windows 11 x64. [Windows 10 status](docs/libusb-charge.md#windows-10-x64-untested)

Jailbreak is optional. [Verify charging on stock iOS or with jailbreak](docs/README.md#verify-charging)

## Quick start

### 1. Check your USB port

Connect the iPad and check that Windows recognizes it. For the first trial, confirm that the port, cable, and any adapter or hub support **1 A at 5 V with USB data**. Stay at the standard USB setting if you can't confirm the rating. [How to check the USB port](docs/README.md#check-the-usb-port)

> [!TIP]
> In my setup, a **USB-A → USB-C adapter** let me use the iPad's original USB-A data cable with a higher-current USB-C port on my PC. Check the ratings of the specific port and adapter you use.

### 2. Download the driver

Download `libusb-win32-bin-1.4.0.2.zip` from the [libusb-win32 release](https://github.com/mcuee/libusb-win32/releases/tag/release_1.4.0.2) and extract the entire ZIP. The app uses this signed package to add a filter for the connected iPad alongside the existing Apple driver. [Verify the download](docs/libusb-charge.md#verify-the-downloads)

### 3. Download the Windows app

If you opened this file from the extracted Windows ZIP, use the folder containing `legacy-ipad-charge.exe`. If you need a fresh copy, download `legacy-ipad-charge-windows-x64.zip` from [Releases](https://github.com/xaer981/legacy-ipad-charge/releases) and extract it. [Verify the download](docs/libusb-charge.md#verify-the-downloads)

### 4. Install the filter and reconnect

If you already installed the filter, run `.\legacy-ipad-charge.exe charge check` from the app folder. If it reports `PASS`, go to step 5. [Troubleshoot an existing filter](docs/libusb-charge.md#troubleshooting)

Open **PowerShell as administrator** from the Start menu. Replace the first path with the folder containing the app and the second with the `bin` folder inside the extracted driver ZIP. You can copy each path from the File Explorer address bar.

```powershell
Set-Location "C:\path\to\legacy-ipad-charge"
.\legacy-ipad-charge.exe filter install --bin "C:\path\to\libusb-win32-bin-1.4.0.2\bin"
```

Keep the path printed after `Rollback file:`. Unplug the iPad, reconnect it to the same USB port, and check the filter.

```powershell
.\legacy-ipad-charge.exe charge check
```

Continue when you see `PASS`. [Installation details](docs/libusb-charge.md#install-the-device-filter) · [If the check fails](docs/libusb-charge.md#troubleshooting)

### 5. Try 1 A, then enable charging

Save the verified 1 A limit for this USB connection.

```powershell
.\legacy-ipad-charge.exe charge confirm-port --max-ma 1000
```

After `PORT_LIMIT_SAVED`, start the 30-second trial. Look for `RAISE_OK` and `RESTORE_OK`.

```powershell
.\legacy-ipad-charge.exe charge trial
```

If both messages appear and charging and USB data worked during the trial, enable 1 A without a timer. [Verify charging on the iPad](docs/README.md#verify-charging)

```powershell
.\legacy-ipad-charge.exe charge enable --limit-ma 1000
```

Look for `ENABLE_OK`. To return to 500 mA, run `.\legacy-ipad-charge.exe charge restore`. [Set up 1.5 A or 2 A](#daily-use) · [Remove the filter](docs/libusb-charge.md#remove-the-device-filter)

## Daily use

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

Run `.\legacy-ipad-charge.exe --help` to list the commands. Use `.\legacy-ipad-charge.exe charge trial --help` for the options and defaults of a specific command.

To share a USB report, run this command and review the JSON before posting. [Compatibility report](docs/compatibility.md#report-your-ipad)

```powershell
.\legacy-ipad-charge.exe report --format json > usb-report.json
```

[Troubleshooting](docs/libusb-charge.md#troubleshooting) · [All guides](docs/README.md)

The ZIP includes `dotnet-THIRD-PARTY-NOTICES.txt`. [Project license](LICENSE)
