# Changelog

## 0.2.0

- Added a Linux x64 CLI using the system `libusb-1.0` with the same charging and telemetry commands as Windows. The packaged CLI requires an identifiable native USB path to request more current. [Linux setup](docs/linux.md)
- Added descriptions to `--help` and detailed help for individual commands, such as `charge trial --help`.
- Added parallel quick starts for Windows and Linux, from checking the USB port through a timed trial and daily charging. [Windows setup](README.md#quick-start-windows) · [Linux setup](README.md#quick-start-linux)
- Added Linux build, test, and packaging checks alongside the Windows workflow.

### Downloads

| Platform | Package | Checksum file |
| --- | --- | --- |
| Windows x64 | `legacy-ipad-charge-windows-x64.zip` | `SHA256SUMS.txt` |
| Linux x64 | `legacy-ipad-charge-linux-x64.tar.gz` | `legacy-ipad-charge-linux-x64.tar.gz.sha256` |

The Windows charging workflow was tested on an iPad 2 GSM. On Kali WSL, a separate libusb probe verified the Apple charging request at 1 A and 2 A. The Linux build and CLI were checked in Kali WSL, and testing charging with the packaged CLI on native Linux is pending. [Test results](docs/compatibility.md)

## 0.1.0

First release with one Windows x64 executable for setup, charging, and diagnostics. [Release notes](https://github.com/xaer981/legacy-ipad-charge/releases/tag/v0.1.0)
