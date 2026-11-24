# USB charging on Windows

The Windows x64 CLI uses the signed libusb-win32 driver as an upper filter for one iPad. Windows keeps the existing Apple USB stack. Check the [device list](compatibility.md) and disconnect any other listed iPads before setup.

[Quick start](../README.md#quick-start) · [All guides](README.md) · [Verify downloads](#verify-the-downloads) · [Install the filter](#install-the-device-filter) · [Charging commands](#charging-commands) · [Troubleshooting](#troubleshooting) · [Remove the filter](#remove-the-device-filter)

## Windows 10 x64 (untested)

Installation, USB data continuity during charging, and rollback still need a hardware test on Windows 10. The signed libusb-win32 package includes an x64 INF section for build 16299 and later. The executable includes .NET 10, whose supported Windows 10 editions are listed by Microsoft. Include your Windows edition and build and the last step that worked in any compatibility report. [Driver release](https://github.com/mcuee/libusb-win32/releases/tag/release_1.4.0.2) · [.NET Windows support](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions) · [Report your iPad](compatibility.md#report-your-ipad)

## Check the USB port

Confirm the lowest current rating for the port, cable, and any adapter or hub at 5 V with data. Then save that rating for the connected iPad's USB path. [How to check the USB port](README.md#check-the-usb-port)

## Verify the downloads

Open PowerShell in the folder where you saved the ZIP files and check their SHA-256 hashes before extracting them.

```powershell
Get-FileHash .\libusb-win32-bin-1.4.0.2.zip -Algorithm SHA256
Get-FileHash .\legacy-ipad-charge-windows-x64.zip -Algorithm SHA256
```

- **Driver ZIP.** The expected SHA-256 is `00004C92CDB99BE36E17FB2377165EB97E63B48BA895BFC04A642EA9C3E26D94`. [libusb-win32 1.4.0.2](https://github.com/mcuee/libusb-win32/releases/tag/release_1.4.0.2)
- **Windows app ZIP.** Compare the hash with the value for `legacy-ipad-charge-windows-x64.zip` in `SHA256SUMS.txt` from the same release. [Project releases](https://github.com/xaer981/legacy-ipad-charge/releases)

## Save the port limit

Connect the iPad to the port you checked. From the extracted ZIP folder, save a limit at or below the lowest confirmed rating in milliamps. For the first trial, save 1,000 mA if the port, cable, and any adapter all support it.

```powershell
.\legacy-ipad-charge.exe charge confirm-port --max-ma 1000
```

`PORT_LIMIT_SAVED` prints the saved limit and the Windows USB path. Run the command again when the confirmed limit changes. Use the same Windows account for saving a limit and running charging commands. Ratings under `%LOCALAPPDATA%\LegacyIpadCharge\PortLimits` from previous versions are still accepted. Recheck the rating after swapping a cable or adapter because the Windows path can stay the same.

## Charging commands

| Command | Default request | Return to 500 mA |
| --- | --- | --- |
| [`charge enable`](#enable-charging) | 2,000 mA, no timer | Run `charge restore` |
| [`charge trial`](#run-a-timed-trial) | 1,000 mA for 30 seconds | The CLI sends restore when the trial ends |
| [`charge restore`](#restore-500-ma) | 500 mA | Ends an active trial and sends the USB baseline request |

`charge enable` and `charge trial` accept `--limit-ma 1000`, `1500`, or `2000`. Both require a saved rating sufficient for the request. [Save the port limit](#save-the-port-limit)

Use `.\legacy-ipad-charge.exe --help` to list commands and `.\legacy-ipad-charge.exe charge trial --help` to see details for a specific command.

## Install the device filter

1. Download and verify both ZIPs, then extract them. Keep all files in the driver package together. [Download names and hashes](#verify-the-downloads)
2. Connect the iPad and confirm USB data works with the original Apple driver. From the app folder, run `.\legacy-ipad-charge.exe devices` and verify its USB ID in the [device list](compatibility.md).
3. Find PowerShell in the Start menu and choose **Run as administrator**. In the first command, use the folder containing `legacy-ipad-charge.exe`. In the second, use the `bin` folder inside the extracted driver package. The installer saves a backup and prints `Rollback file:` followed by the path you will need to remove the filter. Keep that file and leave the extracted driver package in place.

   ```powershell
   Set-Location "C:\path\to\legacy-ipad-charge"
   .\legacy-ipad-charge.exe filter install --bin "C:\path\to\libusb-win32-bin-1.4.0.2\bin"
   ```

4. Unplug the iPad and reconnect it to the same USB port. Confirm USB data still works and continue after this command reports `PASS`.

   ```powershell
   .\legacy-ipad-charge.exe charge check
   ```

## Upgrade from the PowerShell release

Extract the new ZIP into its own folder and keep the original rollback file and driver package. Connect the iPad to the same USB port and test the existing filter with the new executable.

```powershell
.\legacy-ipad-charge.exe charge check
```

If the command reports `PASS`, continue using the current filter. The CLI accepts the existing rollback file and the port rating saved for the same USB path. Recheck the rating if you changed the cable or adapter. [Troubleshooting](#troubleshooting) · [Filter rollback](#remove-the-device-filter)

## Enable charging

Save the USB path rating and run a trial at the intended current. Keep the iPad connected and run `charge enable` for a longer charging session.

```powershell
.\legacy-ipad-charge.exe charge enable
```

Add `--limit-ma 1500` to request 1.5 A. `ENABLE_OK` confirms completion of the USB transfer. Check charging on the iPad or with a meter. After reconnecting the iPad or restarting Windows, check the USB setup before enabling charging again. [Restore 500 mA](#restore-500-ma)

## Run a timed trial

Confirm USB data works, run `charge check`, and save the verified USB-path rating. Start with a 30-second trial at 1,000 mA.

```powershell
.\legacy-ipad-charge.exe charge trial
```

After observing charging and data at 1,000 mA, confirm the USB path is rated for your intended current and [update the saved limit](#save-the-port-limit). Use `charge trial --limit-ma 2000` for the default 2 A continuous mode. A trial accepts `--seconds` between 1 and 180. `RAISE_OK` and `RESTORE_OK` show that the requests completed. The CLI attempts restore when the trial ends or when you interrupt it with Ctrl+C.

### Stock iOS

Note the charging indicator before, during, and after the trial. A data-capable USB meter can measure input current under the same workload. Check that Windows still sees the iPad with the meter connected. Hardware validation on stock iOS is pending. [Report your iPad](compatibility.md#report-your-ipad)

### Jailbreak and SSH

With existing SSH and `ioreg` access, start sampling in a second terminal before the trial. Replace `ipad` with your host or SSH alias.

```powershell
.\legacy-ipad-charge.exe monitor --source ssh --host ipad --interval 4 --duration 230 --format csv > charge-telemetry.csv
```

Compare `adapter_amperage_ma` before, during, and after the trial. Read `instant_amperage_ma` separately for battery current. [Telemetry fields](diagnostics.md#read-battery-current)

## Troubleshooting

| What you see | What to do next |
| --- | --- |
| `devices` doesn't list the iPad | Unlock it and check that Windows recognizes it with a USB data cable. Run `devices` again with only this iPad connected. [Check the USB ID](compatibility.md) |
| `charge check` doesn't show `PASS` | After installing the filter, unplug the iPad, reconnect it to the same port, and run `charge check` again. If it still fails, collect the error and a [read-only USB report](../README.md#help) before reinstalling. |
| No confirmed current limit, or the request exceeds it | Check the rating of the current port, cable, and adapter, then [save the confirmed limit](#save-the-port-limit). Use a trial current that fits that limit. |
| The trial ended without `RESTORE_OK` | Reconnect the iPad to the same port, run `charge check`, then [restore 500 mA](#restore-500-ma) before trying a higher current again. |

## Restore 500 mA

Run this while the iPad is connected and the filter is active. A saved USB-path rating is optional for restore.

```powershell
.\legacy-ipad-charge.exe charge restore
```

When run in another terminal under the same Windows account and session, it signals an active trial to finish before sending its own restore. If a process terminates before completing restore, reconnect the iPad before retrying and check the baseline with `ioreg` or a USB meter.

## Remove the device filter

[Restore 500 mA](#restore-500-ma) before removing the filter. Use an administrator terminal and the rollback file saved by the installer. Files from previous versions are supported.

```powershell
.\legacy-ipad-charge.exe filter remove --rollback-file "C:\path\to\libusb-rollback.clixml"
```

If the CLI asks you to reconnect, unplug the iPad, reconnect it, and run the same command again. The CLI detaches the saved iPad's filter first. It removes the recorded package, service, and driver file after the binding is out of use. Confirm that Windows still recognizes the iPad with the original Apple driver.

If you closed the installer window, paste `%LOCALAPPDATA%\LegacyIpadCharge\Backups` into the File Explorer address bar while signed in as the account that installed the filter. Look for `libusb-rollback.clixml` inside the folder for that installation. The rollback command checks that the file matches the active installation.

### Recover an interrupted or earlier installation

If staging stopped before recording the package name, or you have a legacy rollback file from before the installer tracked installations, identify the libusb-win32 package with `pnputil /enum-drivers`. Confirm the published `oemNN.inf` name for the correct `libusb0.inf` package and supply it explicitly.

```powershell
.\legacy-ipad-charge.exe filter remove --rollback-file "C:\path\to\libusb-rollback.clixml" --recover-staged-package oem42.inf
```

The CLI checks the package, driver file, service, and bindings before cleaning up.

## Protocol details

The CLI sends a device-recipient vendor OUT request with `bRequest=0x40` and no data stage. Its verified request layout uses `wValue=0` and sets `wIndex` to the active current above 500 mA.

| Request | USB setup bytes |
| --- | --- |
| 500 mA restore | `40 40 00 00 00 00 00 00` |
| 1,000 mA | `40 40 00 00 F4 01 00 00` |
| 1,500 mA | `40 40 00 00 E8 03 00 00` |
| 2,000 mA | `40 40 00 00 DC 05 00 00` |

The 24-byte `libusb_request` uses vendor type `2` for `LIBUSB_IOCTL_VENDOR_WRITE`. [libusb-win32 request layout](https://github.com/mcuee/libusb-win32/blob/release_1.4.0.2/libusb/src/driver/driver_api.h) · [Hardware results](compatibility.md#tested-configuration)
