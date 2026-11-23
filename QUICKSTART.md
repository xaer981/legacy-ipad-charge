# Legacy iPad USB Charging for Windows

This Windows x64 package contains the USB diagnostic CLI and the PowerShell charging scripts. Charging was tested on an iPad 2 GSM (A1396) with Windows 11. [Tested setup and Windows 10 status](docs/compatibility.md)

## Quick start

Connect your iPad with a USB data cable. Open PowerShell in the extracted folder and check that Windows sees the device.

```powershell
.\legacy-ipad-charge.exe devices
.\legacy-ipad-charge.exe report --format json > usb-report.json
```

## Set up charging

1. Confirm that the port, data cable and any adapters support the intended current at 5 V while carrying USB data. [Check the USB port](docs/libusb-charge.md#check-the-usb-port)
2. Install the separately downloaded signed filter and save the rollback file. [Install the filter](docs/libusb-charge.md#install-the-device-filter)
3. **Unplug the iPad and reconnect it to the same USB port.** Run this command from the extracted folder and continue after it reports `PASS`.

   ```powershell
   .\tools\Invoke-LibusbChargeTrial.ps1 -CheckDevice
   ```

4. Save the confirmed port limit and run a timed charging trial. [Save the port limit](docs/libusb-charge.md#save-the-port-limit) · [Run a timed trial](docs/libusb-charge.md#run-a-timed-trial)

When removing an active filter, unplug and reconnect the iPad when prompted, then run the same rollback command again. [Remove the filter](docs/libusb-charge.md#remove-the-device-filter)

See the [Windows 10 notes (untested)](docs/libusb-charge.md#windows-10-x64-untested) before using the package on Windows 10.

The included executable contains the .NET runtime. The ZIP includes `dotnet-THIRD-PARTY-NOTICES.txt`. [Project license](LICENSE)
