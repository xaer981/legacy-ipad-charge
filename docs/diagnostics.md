# Diagnostics and battery telemetry

[All guides](README.md) · [Windows troubleshooting](libusb-charge.md#troubleshooting) · [Linux troubleshooting](linux.md#troubleshooting)

Run the Windows examples from the extracted ZIP folder. On Linux, use `./legacy-ipad-charge` with the same arguments. The executables include the .NET runtime. [Linux setup](linux.md)

## Inspect an iPad

```powershell
.\legacy-ipad-charge.exe devices
.\legacy-ipad-charge.exe inspect --vid 05AC --pid 12A2
.\legacy-ipad-charge.exe probe --vid 05AC --pid 12A2 --dry-run
.\legacy-ipad-charge.exe report --format csv
```

Use `devices` to find an iPad's VID and PID and pass them to `inspect`. The example USB ID belongs to the iPad 2 GSM. On Windows, inventory includes the physical USB device, its `MI_XX` functions, driver services and filters, interface GUIDs, and port topology. `portChain` starts at the root hub, and `connectionStatus` explains failures to read hub-port data. On Linux, inventory reports USB IDs, port chains, physical paths where available, and USB authorization status. The CSV report omits device serial numbers and instance IDs.

On Windows, `probe --dry-run` reads PnP information about the WinUSB path. `winUsbServiceSeen` describes the PnP service assignment and `winRtUsbRestricted` reports the WinRT USB restriction when available. On Linux, inventory and `probe --dry-run` read sysfs. A physical USB path is shown for a supported unambiguous PCI USB controller and port chain. Use `charge check` to verify that the charging transport can open the selected iPad.

## Read battery current

On the test iPad, this workflow used an existing jailbreak and SSH access. For an iPad running stock iOS, follow the [stock iOS verification steps](libusb-charge.md#stock-ios).

If you already have shell access on the iPad, save the output of `ioreg -r -l -w 0 -c AppleARMPMUCharger` as a UTF-8 text file. The CLI can parse that snapshot or sample over SSH. Replace `ipad` with your SSH host alias or `user@host`.

```powershell
.\legacy-ipad-charge.exe report --format json --input charger.txt
.\legacy-ipad-charge.exe monitor --source ssh --host ipad --interval 20 --duration 180 --format csv
```

`--input -` reads the snapshot from stdin. Live sampling requires OpenSSH on Windows or Linux, working key-based access, and a verified host key in `known_hosts`. Use an SSH alias, the default key, `ssh-agent`, or `--identity <file>`. The CLI runs a fixed remote command and checks the existing host key without writing to `known_hosts`. You can import a saved snapshot if the OpenSSH versions cannot connect. `monitor --format json` writes one JSON object per line.

`monitor` logs failed reads to stderr and continues sampling. The JSON or CSV stream contains successful readings. A snapshot needs at least one usable value and may contain at most one AppleARMPMUCharger node. If the whole window contains no successful readings, the command exits with code `1`.

`instantAmperageMa` is battery current. Positive readings indicated charging on the test iPad and negative readings indicated discharge. `adapterDetails` contains iOS's reported source parameters. `isCharging` is a separate iOS flag. Temperature is reported in the original `ioreg` units. Missing values appear as `null` in JSON or an empty CSV cell.

Exit codes are `0` for a result, `1` for a read or format error or timeout, `2` for invalid arguments, `3` for no matching device (`inspect` and `probe`), and `130` for interruption.

## Build troubleshooting

When building from the source checkout, pass the platform explicitly so a shell value such as `Platform=HPD` cannot select a different build directory. If `dotnet --version` shows an older SDK, use the .NET 10 SDK's `dotnet.exe` by full path. For packaging, pass that path with `.\tools\Build-Release.ps1 -Dotnet 'C:\path\to\dotnet.exe'`. To skip the SDK's first-run changes to the PATH and development certificate, set `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH` and `DOTNET_GENERATE_ASPNET_CERTIFICATE` to `false` before building.
