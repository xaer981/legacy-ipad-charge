# Legacy iPad USB Charging for Windows

**Charge an iPad 2 GSM (A1396) over USB while it stays connected to your Windows 11 PC for data.**

If your iPad shows “Not Charging” or loses battery while connected to your PC, this utility lets you request more charging current from a compatible USB port.

![Median battery current went from −647 mA before the trial to +201 mA during it.](docs/assets/battery-current.png)

Median battery current on a jailbroken **iPad 2 GSM (`A1396`)** at a reported **1,500 mA** limit during a three-minute Windows 11 x64 trial. The test used `ioreg` and the USB data session stayed active. A later trial held the reported **2,000 mA** limit for 30 minutes and confirmed the return to 500 mA. [Test setup and measurements](docs/compatibility.md#tested-configuration)

[Get the Windows ZIP](#get-the-windows-zip) · [Set up charging](#charging-on-windows) · [Check compatibility](docs/compatibility.md) · [Run diagnostics](#quick-start) · [Support](#support-the-project)

## Get the Windows ZIP

The Windows x64 ZIP contains the diagnostic executable, charging scripts, and a short setup guide. See [GitHub Releases](https://github.com/xaer981/legacy-ipad-charge/releases) for published downloads, or [build the ZIP locally](#build-and-test). Download the signed libusb-win32 filter separately using the [charging guide](docs/libusb-charge.md#install-the-device-filter).

## Charging on Windows

The charging script targets **iPad 2 GSM (`05AC:12A2`)** on Windows 11 x64. Testing on stock iOS is pending. [Compatibility](docs/compatibility.md)

**Windows 10 x64 is untested.** See the [Windows 10 notes](docs/libusb-charge.md#windows-10-x64-untested) for the driver checks and how to share a test result.

1. **Check the exact port before raising the limit.** Look up the current it supports at 5 V while carrying USB data. A standard USB-A data port gives a USB 2.0 iPad a 500 mA budget. To request 2,000 mA, confirm that the port, cable, and any adapters support at least 2 A at 5 V. [Check the USB port](docs/libusb-charge.md#check-the-usb-port)
2. Install the signed libusb-win32 1.4.0.2 x64 per-device filter alongside Apple's driver and save the rollback file. [Installing the filter](docs/libusb-charge.md#install-the-device-filter)
3. **Unplug the iPad and reconnect it to the same USB port.** Run the check below from the project or extracted release folder. Continue after it reports `PASS`.

   ```powershell
   .\tools\Invoke-LibusbChargeTrial.ps1 -CheckDevice
   ```

4. With the iPad connected to that port, save the confirmed limit. This example records a USB path rated for at least 2 A at 5 V. Set `-PortMaxMa` to the lowest rating in your path, in milliamps. [Save the port limit](docs/libusb-charge.md#save-the-port-limit)

   ```powershell
   .\tools\Invoke-LibusbChargeTrial.ps1 -ConfirmPort -PortMaxMa 2000
   ```

5. If the saved limit is at least 1 A, run a 30-second trial at 1,000 mA, then test the current you plan to use. [Run a timed trial](docs/libusb-charge.md#run-a-timed-trial)

Run `-Enable` from the project folder in 64-bit PowerShell to request **2,000 mA**. The script checks the saved limit for the connected USB path before sending the request and exits without a timer. `-Enable` and `-RunOnce` reject unknown paths and requests above the saved limit.

```powershell
.\tools\Invoke-LibusbChargeTrial.ps1 -Enable
```

Use `-LimitMa` to select **1,000, 1,500, or 2,000 mA**. For example, this command selects 1,500 mA.

```powershell
.\tools\Invoke-LibusbChargeTrial.ps1 -Enable -LimitMa 1500
```

Run `-Restore` to return to 500 mA. It works regardless of whether the USB path has a saved limit.

```powershell
.\tools\Invoke-LibusbChargeTrial.ps1 -Restore
```

When removing an active filter, unplug and reconnect the iPad when prompted, then run the same rollback command again. [Remove the filter](docs/libusb-charge.md#remove-the-device-filter)

[Charging commands](docs/libusb-charge.md#charging-commands) · [Restore 500 mA](docs/libusb-charge.md#restore-500-ma)

### Stock iOS

The script runs on Windows over USB. End-to-end testing on stock iOS is pending. Check the charging indicator or use a USB meter that passes data to measure input current. [Stock iOS verification](docs/libusb-charge.md#stock-ios)

### Jailbreak and SSH

Use the same charging commands. With SSH and `ioreg` access, you can also read the adapter limit and battery current before, during, and after a trial. [Battery telemetry](docs/libusb-charge.md#jailbreak-and-ssh)

## Quick start

On Windows 11 x64, connect your iPad with a USB data cable. Extract the Windows ZIP and open PowerShell in the extracted folder. The executable includes the .NET runtime.

```powershell
.\legacy-ipad-charge.exe devices
.\legacy-ipad-charge.exe report --format json > usb-report.json
```

To run from source, install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and open PowerShell in the project directory.

```powershell
dotnet run --project src/LegacyIpadCharge -p:Platform=AnyCPU -- devices
dotnet run --project src/LegacyIpadCharge -p:Platform=AnyCPU -- report --format json > usb-report.json
```

The CLI reports USB IDs, connection speed, port topology, and the Windows driver stack. It runs on Windows with the stock Apple driver. If the report contains `[]`, Windows did not expose any matching Apple USB devices to the CLI. Check the data cable and look for the iPad in Device Manager, then run the report again. See [diagnostics and telemetry](docs/diagnostics.md) for filtered inspection and battery measurements.

If you have another 30-pin or Lightning iPad, the read-only USB report can help document compatibility. [Report your iPad](docs/compatibility.md#report-your-ipad)

## Build and test

Use the .NET 10 SDK. Check `dotnet --version` before building. [Build troubleshooting](docs/diagnostics.md#build-troubleshooting)

```powershell
dotnet build LegacyIpadCharge.slnx '-p:Platform=Any CPU' -warnaserror
dotnet run --no-build --project tests/LegacyIpadCharge.Tests -p:Platform=AnyCPU
powershell.exe -NoProfile -File .\tests\Test-LibusbWorkflows.ps1
```

The tests run without an iPad. Build the user ZIP from the project root. The same command runs in Windows CI.

```powershell
.\tools\Build-Release.ps1
```

The ZIP is written to `bin/release/legacy-ipad-charge-windows-x64.zip`. Its SHA-256 is printed at the end.

## Development notes

The [WinUSB probe](tools/LegacyIpadCharge.GetStatusProbe) and [KMDF filter](tools/LegacyIpadCharge.StatusFilter) reproduce earlier experiments. They are reserved for the lab and excluded from the Windows ZIP. [Lab limitations](docs/test-plan.md#фильтр-get_status-в-windows-11-x64-в-virtualbox)

- [Compatibility and community reports](docs/compatibility.md)
- [Lab results (Russian)](docs/feasibility.md)
- [Lab test plan (Russian)](docs/test-plan.md)
- [Risks and limitations (Russian)](docs/risk-assessment.md)
- [Protocol and driver references](docs/references.md)

## Support the project

If this tool helped you keep an iPad in use, you can [support its development on Boosty](https://boosty.to/xaer981) with a one-time donation. Support helps with USB protocol research, testing on more iPad models, and maintenance.

You can also help by sharing a [compatibility report](docs/compatibility.md#report-your-ipad).

## License

The project uses the [MIT License](LICENSE). Third-party software has its own license terms. [Sources and notices](docs/references.md)
