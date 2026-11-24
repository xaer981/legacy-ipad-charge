# USB charging on Linux x64

Use a compatible USB data connection to charge an older iPad. The CLI uses the Linux USB stack through the system `libusb-1.0` library. [Windows setup](https://github.com/xaer981/legacy-ipad-charge/blob/main/docs/libusb-charge.md) · [Device IDs and test results](https://github.com/xaer981/legacy-ipad-charge/blob/main/docs/compatibility.md)

[Download](#1-get-the-cli) · [USB access](#2-check-detection-and-access) · [First trial](#3-try-a-higher-current) · [Daily use](#4-enable-charging-for-daily-use) · [Troubleshooting](#troubleshooting)

> [!NOTE]
> A separate libusb probe verified the Apple request on an iPad 2 over USB/IP in Kali WSL. Hardware verification of this CLI on native Linux is pending. Use the CLI in WSL for diagnostics. Higher-current commands require a physical USB path visible in Linux. [Test results](https://github.com/xaer981/legacy-ipad-charge/blob/main/docs/compatibility.md#linux)

## 1. Get the CLI

Download `legacy-ipad-charge-linux-x64.tar.gz` and `legacy-ipad-charge-linux-x64.tar.gz.sha256` from [Releases](https://github.com/xaer981/legacy-ipad-charge/releases). If the archive is missing, [build it from source](#build-from-source). The archive includes the .NET runtime. For charging, install the shared library `libusb-1.0.so.0` from your distribution. On Debian, Ubuntu, and Kali, the package is `libusb-1.0-0`.

```bash
sha256sum -c legacy-ipad-charge-linux-x64.tar.gz.sha256
mkdir -p legacy-ipad-charge-linux
tar -xf legacy-ipad-charge-linux-x64.tar.gz -C legacy-ipad-charge-linux
cd legacy-ipad-charge-linux
./legacy-ipad-charge --version
./legacy-ipad-charge --help
```

For command options and defaults, run `./legacy-ipad-charge charge trial --help` or append `--help` to another command.

## 2. Check detection and access

Connect one supported iPad with a USB data cable and unlock it. Check that Linux sees the device and that the CLI can open its USB connection.

```bash
./legacy-ipad-charge devices
./legacy-ipad-charge charge check
```

If `devices` lists your iPad and `charge check` reports permission denied, use your distribution's USB permissions for that device. On a desktop with systemd and an active local session, you can add the following line to `/etc/udev/rules.d/70-legacy-ipad-charge.rules` with administrator permissions. `12a2` is the iPad 2 GSM product ID. For a different model, substitute its own listed USB product ID.

```udev
SUBSYSTEM=="usb", ENV{DEVTYPE}=="usb_device", ATTR{idVendor}=="05ac", ATTR{idProduct}=="12a2", TAG+="uaccess"
```

Run `sudo udevadm control --reload`, reconnect the iPad to the same port and repeat `charge check`. For a remote or headless session, use your distribution's USB group policy for the same VID/PID. Use the same user account for all charging commands. You can remove the rule file, reload the rules and reconnect the iPad to undo the permission change.

Continue once `charge check` reports `PASS` and `devices` shows a physical `locationPath`. Forwarded USB/IP devices and paths the CLI cannot identify are blocked for higher-current requests. Use a native Linux host with a supported PCI USB controller. `charge restore` can still return an accessible iPad to its baseline.

## 3. Try a higher current

Confirm that the **port, cable, and every adapter or hub** support **1 A at 5 V while carrying USB data** before the first trial. The CLI saves the number you enter for that physical USB path. Recheck the rating whenever you change a component, even if the reported path stays the same. [Checking the USB path](https://github.com/xaer981/legacy-ipad-charge/blob/main/docs/README.md#check-the-usb-port)

```bash
./legacy-ipad-charge charge confirm-port --max-ma 1000
./legacy-ipad-charge charge trial --limit-ma 1000 --seconds 30
```

Look for `RAISE_OK` and `RESTORE_OK` and confirm USB data stays available. `charge restore` from the same user account or Ctrl+C ends an active trial. If a process is killed before it can restore, reconnect the iPad and check the baseline before trying again.

## 4. Enable charging for daily use

After a successful trial, use `charge enable --limit-ma 1000` to keep that setting. For the default **2 A**, confirm that the entire USB path supports 2 A at 5 V with data, then run these commands.

```bash
./legacy-ipad-charge charge confirm-port --max-ma 2000
./legacy-ipad-charge charge trial --limit-ma 2000
./legacy-ipad-charge charge enable
```

`charge enable` leaves the selected limit active without a timer. After a reconnect or reboot, use `charge check` before enabling it again. To end the session, request 500 mA. This works without a saved port rating.

```bash
./legacy-ipad-charge charge restore
```

## Verify charging

- **Stock iOS.** Use the charge indicator and battery percentage under the same workload. A USB meter with data passthrough can measure input current and voltage.
- **Jailbroken iPad with existing SSH access.** Start monitoring in a second terminal before a trial. Replace `ipad` with your SSH alias or `[user@]host`. The `adapter_amperage_ma` field records the limit reported by iOS, and `instant_amperage_ma` records battery current. [SSH requirements and telemetry fields](https://github.com/xaer981/legacy-ipad-charge/blob/main/docs/diagnostics.md#read-battery-current)

  ```bash
  ./legacy-ipad-charge monitor --source ssh --host ipad --interval 4 --duration 230 --format csv
  ```

## Troubleshooting

| What you see | What to check |
| --- | --- |
| `devices` shows an empty list | Unlock the iPad, verify the USB cable carries data, and check whether Linux sees the iPad. |
| Missing `libusb-1.0.so.0` or permission denied | Install your distribution's libusb runtime or [set USB permissions for your iPad](#2-check-detection-and-access). |
| `locationPath` is `null` | For a charging trial, use a physical USB port on a supported native Linux controller. |
| The request exceeds the confirmed rating | Use a lower limit or save a verified rating for this USB path under the same user account. |
| The trial ended without `RESTORE_OK` | Reconnect the iPad, run `charge check`, then `charge restore` before trying a higher limit again. |

## Share a compatibility report

Run this read-only command and review the JSON before sharing it.

```bash
./legacy-ipad-charge report --format json > usb-report.json
```

Include the Linux distribution and kernel, iPad model, iOS version, USB path, and the charging and restore results. [Report your iPad](https://github.com/xaer981/legacy-ipad-charge/blob/main/docs/compatibility.md#report-your-ipad)

## Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and `python3`. Run these commands from the source checkout.

```bash
dotnet build LegacyIpadCharge.slnx '-p:Platform=Any CPU' -warnaserror
dotnet run --no-build --project tests/LegacyIpadCharge.Tests -p:Platform=AnyCPU
bash tools/Build-LinuxRelease.sh
```

The archive and checksum are written to `bin/release`.
