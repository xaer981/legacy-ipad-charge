# USB charging on Windows

This guide covers the iPad 2 GSM (`A1396`, `05AC:12A2`) on Windows 11 x64. The script uses the signed libusb-win32 driver as an upper filter on that iPad. Windows keeps `usbccgp` as the parent driver and retains the Apple USB stack.

[Windows 10](#windows-10-x64-untested) · [Check the USB port](#check-the-usb-port) · [Save the port limit](#save-the-port-limit) · [Install the filter](#install-the-device-filter) · [Charging commands](#charging-commands) · [Run a trial](#run-a-timed-trial) · [Stock iOS](#stock-ios) · [Jailbreak and SSH](#jailbreak-and-ssh) · [Remove the filter](#remove-the-device-filter)

## Before you start

- Confirm that Windows recognizes the iPad over USB with the Apple driver.
- The trial script runs on Windows and sends the same USB request with either iOS setup. The [hardware trial](compatibility.md#tested-configuration) used a jailbroken iPad. A stock iOS charging trial is pending.
- Installation and rollback from the release ZIP were verified on the test PC with Secure Boot enabled, after removing a previous filter installation. Testing on a fresh Windows installation and with Memory Integrity enabled is pending. [Installer test](compatibility.md#filter-installation-and-rollback)

## Windows 10 x64 (untested)

Windows 10 is a candidate for community testing. The signed libusb-win32 1.4.0.2 package has an x64 INF section for Windows 10 build 16299 and later. Filter installation, USB data continuity during charging, and rollback on Windows 10 still need a hardware test. [Driver release](https://github.com/mcuee/libusb-win32/releases/tag/release_1.4.0.2) · [INF version matching](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-manufacturer-section)

On Windows 10 22H2 x64, use the PowerShell PnP checks in the [installation steps](#install-the-device-filter). After a successful check, follow the same [timed trial](#run-a-timed-trial), [500 mA restore](#restore-500-ma), and [filter removal](#remove-the-device-filter) steps.

The charging script runs in Windows PowerShell 5.1. The optional diagnostic CLI uses .NET 10, whose Windows 10 support depends on the edition. Reports are welcome even if the CLI does not start. Include the Windows edition and build, the Apple driver version, and the last step that worked. [Microsoft .NET support matrix](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions) · [Report your iPad](compatibility.md#report-your-ipad)

## Check the USB port

1. Find the 5 V current rating for the physical port in the PC, motherboard, or hub manual. Look for a rating that applies while that port carries USB data. Confirm the data cable and any adapters support the chosen current at 5 V. Use the lowest rating in the path.
2. Budget **500 mA** for a USB 2.0 iPad on a standard USB-A data port, including a standard USB 3.x Type-A port. A port documented as **BC 1.2 CDP** supports **1,500 mA** with data. To request **2,000 mA**, confirm support for at least 2 A at 5 V with data on the specific port. If support for a higher current is unknown, stay with the 500 mA baseline. [USB-IF port limits](https://compliance.usb.org/index.asp?Format=Standard&UpdateFile=Policies) · [USB-IF charging port tests](https://compliance.usb.org/index.asp?UpdateFile=Battery+Charging&tgt=post)
3. Use `inspect` to identify the USB path and `-CheckDevice` to verify that the filter matches the connected iPad. Get the electrical rating from the hardware specifications. `bMaxPower` comes from the device descriptor, and `ioreg` reports the adapter limit recognized by iOS. A data-capable USB meter can measure input voltage and current during the trial. [Diagnostics](diagnostics.md#inspect-an-ipad) · [Timed trial](#run-a-timed-trial)

## Save the port limit

Connect the iPad to the port you verified. From the project folder in 64-bit PowerShell, pass the lowest confirmed current rating at 5 V as `-PortMaxMa` in milliamps. The example below is for a USB path rated for at least 2 A.

```powershell
.\tools\Invoke-LibusbChargeTrial.ps1 -ConfirmPort -PortMaxMa 2000
```

`PORT_LIMIT_SAVED` reports the confirmed limit and the full USB path from Windows. The command stores the rating under `%LOCALAPPDATA%\LegacyIpadCharge\PortLimits` for the current Windows account and sends no USB request. Run it again to update the rating for the same path. Recording 500 mA blocks elevated charging on that path.

`-Enable` and `-RunOnce` compare the requested current with the saved value for the current USB path before sending a request. Moving the iPad to an unconfirmed port blocks charging. A passive cable or adapter swap on the same port can leave the Windows path unchanged. Recheck and update the saved rating when the hardware changes.

## Charging commands

Run `tools/Invoke-LibusbChargeTrial.ps1` in 64-bit PowerShell from the project folder. For `-Enable` and `-RunOnce`, `-LimitMa` accepts 1000, 1500, or 2000.

| Mode | Default request | Return to 500 mA |
| --- | --- | --- |
| [`-Enable`](#enable-charging) | 2,000 mA, no timer | Run `-Restore` |
| [`-RunOnce`](#run-a-timed-trial) | 1,000 mA for 30 seconds | The script sends the restore request when the trial ends |
| [`-Restore`](#restore-500-ma) | 500 mA | Ends an active trial and sends the USB baseline request |

Set `-LimitMa` explicitly in scripts that rely on a particular current. Both charging modes require a saved rating sufficient for the requested current. [Save the port limit](#save-the-port-limit)

## Install the device filter

Use 64-bit Windows PowerShell as administrator and open the project or release folder. These steps are for a PC without an existing libusb-win32 installation.

1. Connect the iPad and verify USB data works with the Apple driver. Run both preflight commands before installing any driver.

   ```powershell
   .\tools\Invoke-LibusbChargeTrial.ps1
   .\tools\Remove-LibusbChargeFilter.ps1 -CheckEnvironment
   ```

   If PowerShell blocks the scripts, stop here. Verify the source of the files and follow the execution policy for your machine. [PowerShell execution policies](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies?view=powershell-5.1)

2. Record the PnP properties before installation. Compare the same properties after reconnecting in step 4. This check is available on both Windows 10 and Windows 11.

   ```powershell
   $ipads = @(Get-PnpDevice -PresentOnly | Where-Object InstanceId -Match '^USB\\VID_05AC&PID_12A2\\[^\\\s]+$')
   if ($ipads.Count -ne 1 -or $ipads[0].Status -ne 'OK') { throw 'Expected exactly one ready iPad 2 GSM.' }
   Get-PnpDeviceProperty -InstanceId $ipads[0].InstanceId |
       Where-Object KeyName -in @(
           'DEVPKEY_Device_Service'
           'DEVPKEY_Device_DriverInfPath'
           'DEVPKEY_Device_UpperFilters'
           'DEVPKEY_Device_LowerFilters'
       ) |
       Format-Table KeyName, Data -AutoSize
   ```

3. Download `libusb-win32-bin-1.4.0.2.zip` from the [official release](https://github.com/mcuee/libusb-win32/releases/tag/release_1.4.0.2). Run `Get-FileHash -Algorithm SHA256` on the ZIP. The expected SHA-256 is `00004C92CDB99BE36E17FB2377165EB97E63B48BA895BFC04A642EA9C3E26D94`. Extract the whole archive and keep the original INF, catalog, and binaries together.

   Pass its extracted `bin` directory to the installer. It checks the installer and driver files, backs up the Apple package, and prints the rollback path before staging libusb-win32. Keep the rollback file and the extracted driver files. [Windows driver catalogs](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/catalog-files)

   ```powershell
   .\tools\Install-LibusbChargeFilter.ps1 -Bin 'C:\path\to\libusb-win32-bin-1.4.0.2\bin'
   ```

4. Unplug the iPad and reconnect it to the same USB port. Repeat the PnP property check from step 2. Expect `usbccgp` as the service, the original driver INF and Apple filters, and `libusb0` among the upper filters. Confirm USB data still works, then run `.\tools\Invoke-LibusbChargeTrial.ps1 -CheckDevice` and continue after it reports `PASS`.

## Enable charging

Save the USB path's confirmed rating and run a short trial at the intended current. For a longer session, keep the iPad connected and run the enable command. [Save the port limit](#save-the-port-limit) · [Timed trial](#run-a-timed-trial)

```powershell
.\tools\Invoke-LibusbChargeTrial.ps1 -Enable
```

For a session at 1,500 mA, pass the limit explicitly.

```powershell
.\tools\Invoke-LibusbChargeTrial.ps1 -Enable -LimitMa 1500
```

`ENABLE_OK` means Windows completed the USB transfer. Use `-Restore` to send the baseline request. After reconnecting or restarting, check the current setup before enabling again. [Restore 500 mA](#restore-500-ma)

## Run a timed trial

Verify the port, cable, and adapters support the trial current at 5 V and save the rating. [Check the USB port](#check-the-usb-port) · [Save the port limit](#save-the-port-limit)

1. From the project root in 64-bit Windows PowerShell 5.1 or PowerShell 7, run the self-test and check the device binding. The commands check the request layout and the filter handle without sending a USB control transfer.

```powershell
.\tools\Invoke-LibusbChargeTrial.ps1
.\tools\Invoke-LibusbChargeTrial.ps1 -CheckDevice
```

2. Decide how to observe charging before starting the trial and keep the iPad workload consistent. Use the stock iOS steps if you do not have SSH and `ioreg` access. [Stock iOS](#stock-ios) · [Jailbreak and SSH](#jailbreak-and-ssh)

3. Start with the default 30-second trial at 1,000 mA.

```powershell
.\tools\Invoke-LibusbChargeTrial.ps1 -RunOnce
```

The script verifies the filter handle against the connected iPad before sending the request. It attempts to restore 500 mA when the timer ends or when you run `-Restore` in another PowerShell window in the same Windows session and user account. `RAISE_OK` and `RESTORE_OK` report completion of the USB transfers. Verify charging using the observations below.

The lab also exercised these combinations. Use a USB path rated for the selected current. [Lab results](compatibility.md#tested-configuration)

```powershell
.\tools\Invoke-LibusbChargeTrial.ps1 -RunOnce -LimitMa 1000 -Seconds 180
.\tools\Invoke-LibusbChargeTrial.ps1 -RunOnce -LimitMa 1500 -Seconds 30
.\tools\Invoke-LibusbChargeTrial.ps1 -RunOnce -LimitMa 1500 -Seconds 180
.\tools\Invoke-LibusbChargeTrial.ps1 -RunOnce -LimitMa 2000 -Seconds 180
```

## Stock iOS

1. Start from a freshly connected iPad. Note the charge icon or **Not Charging** message before, during, and after the trial. A short trial may leave the battery percentage unchanged. [Apple's charging guide](https://support.apple.com/en-afri/guide/ipad/ipad997da965/ipados)
2. If you have an inline USB meter rated for the trial current, use one that passes USB data. Verify Windows still recognizes the iPad with the meter in place. Compare USB input current before the trial, during it, and after the restore under the same workload. The charging icon is a qualitative observation.
3. Save the trial output and your observations. The Windows log can confirm that the transfers completed. If you have only the log and screen observations, report the current and adapter limit as unmeasured. [Report your iPad](compatibility.md#report-your-ipad)

## Jailbreak and SSH

Use your existing SSH and [`ioreg` access](diagnostics.md#read-battery-current). In a second PowerShell window, start sampling before the trial. For a three-minute trial, this command leaves time to capture the baseline and restore. Replace `ipad` with your SSH host alias. If you use the Windows ZIP, replace `dotnet run --project src/LegacyIpadCharge -p:Platform=AnyCPU --` with `.\legacy-ipad-charge.exe`.

```powershell
dotnet run --project src/LegacyIpadCharge -p:Platform=AnyCPU -- monitor --source ssh --host ipad --interval 4 --duration 230 --format csv |
    Tee-Object -FilePath .\charge-telemetry.csv
```

Check that `adapter_amperage_ma` starts at 500 mA, changes to the requested limit, and returns to 500 mA after `RESTORE_OK`. Record `instant_amperage_ma` separately to see whether the battery is charging or discharging.

## Restore 500 mA

With the iPad connected and the filter loaded, run the manual restore if needed. A `-Restore` from the same Windows session signals an active trial to finish before sending its own 500 mA request. It waits up to 15 seconds to acquire the operation lock. A saved USB port limit is optional for restore.

```powershell
.\tools\Invoke-LibusbChargeTrial.ps1 -Restore
```

If the process terminated before restoring the limit or the restore command failed, reconnect the iPad before repeating a trial. With SSH telemetry, check the reported 500 mA baseline. With a USB meter, compare the input current with the pretrial reading under the same workload. Without either reading, record that the baseline could not be measured.

## Remove the device filter

If you enabled charging, [restore 500 mA](#restore-500-ma) before removing the filter. In an administrator PowerShell, run the rollback command with the file saved before installation.

```powershell
.\tools\Remove-LibusbChargeFilter.ps1 -RollbackFile 'C:\path\to\libusb-rollback.clixml'
```

If the command asks you to reconnect the iPad, do so and run it again. It detaches the saved iPad's filter first and cleans up the recorded package, service and file once the driver is no longer in use. Confirm Windows recognizes the iPad with its original Apple driver package after rollback.

### Recover an interrupted or earlier installation

The installer keeps the current installation ID and the staged `oemNN.inf` name in `%WINDIR%\System32\drivers\LegacyIpadCharge-libusb.clixml`. Each new rollback file identifies one installation.

If staging stopped before the published package name was recorded, or you have a rollback file from before the installer script was added, identify the package first.

```powershell
Get-WindowsDriver -Online | Where-Object { [IO.Path]::GetFileName($_.OriginalFileName) -eq 'libusb0.inf' }
```

Confirm the listed package belongs to the installation you want to remove. Pass its `Driver` value as `-RecoverStagedPackage`. The rollback also checks the matching package hash and device binding. The example uses `oem42.inf`.

```powershell
.\tools\Remove-LibusbChargeFilter.ps1 -RollbackFile 'C:\path\to\libusb-rollback.clixml' -RecoverStagedPackage oem42.inf
```

## Protocol details

The script sends one device-recipient vendor OUT request with no data stage. `bRequest=0x40` identifies the Apple request. `wValue=0` sets zero additional current in suspend. `wIndex` is the additional current above the 500 mA baseline while active. [Apple MFi R9, tables 2-8 and 2-9](https://www.scribd.com/document/694737660/MFiAccessoryHardwareSpecificationR9NoRestriction-875543417)

| Action | `wIndex` | USB setup bytes |
| --- | ---: | --- |
| Restore 500 mA | 0 | `40 40 00 00 00 00 00 00` |
| Request 1,000 mA | 500 | `40 40 00 00 F4 01 00 00` |
| Request 1,500 mA | 1,000 | `40 40 00 00 E8 03 00 00` |
| Request 2,000 mA | 1,500 | `40 40 00 00 DC 05 00 00` |

The script passes a 24-byte `libusb_request` to `LIBUSB_IOCTL_VENDOR_WRITE`. The filter converts the vendor type (`2`) to `bmRequestType=0x40` on USB. [libusb-win32 request layout](https://github.com/mcuee/libusb-win32/blob/release_1.4.0.2/libusb/src/driver/driver_api.h) · [Request type conversion](https://github.com/mcuee/libusb-win32/blob/release_1.4.0.2/libusb/src/windows.c)

`AdapterDetails` is the limit the iPad reports and `InstantAmperage` is battery current. See the [lab results](feasibility.md#трёхминутное-испытание-до-1-а-на-основном-пк) for both readings.
