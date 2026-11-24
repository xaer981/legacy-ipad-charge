using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Xml;
using LegacyIpadCharge;

if (args is ["--version"])
{
    Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));
    return 0;
}

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    stopping.Cancel();
};
string? command = null;
try
{
    var helpTopic = args switch
    {
        [] or ["--help" or "-h"] => "",
        ["charge" or "filter"] => args[0],
        [var topic, "--help" or "-h"] => topic,
        ["charge" or "filter", var action, "--help" or "-h"] => $"{args[0]} {action}",
        _ => null
    };
    if (helpTopic is not null)
    {
        if (!CliHelp.Write(Console.Out, helpTopic))
            throw new UsageError($"Unknown help topic '{helpTopic}'.");
        return 0;
    }

    var grouped = args[0] is "charge" or "filter";
    command = grouped ? $"{args[0]} {args[1]}" : args[0];
    var options = ParseOptions(command, args[(grouped ? 2 : 1)..]);
    if (grouped)
    {
        if (OperatingSystem.IsWindows())
        {
            if (RuntimeInformation.OSArchitecture != Architecture.X64 || !Environment.Is64BitProcess)
                throw new IOException("Charging and filter management require Windows x64 and the x64 CLI.");
            RunWindowsCommand(command, options, stopping.Token);
        }
        else if (OperatingSystem.IsLinux() && args[0] == "charge")
        {
            using var termination = command == "charge trial" ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal =>
            {
                signal.Cancel = true;
                stopping.Cancel();
            }) : null;
            var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(data))
                throw new IOException("Cannot locate the user data directory for Linux charging settings.");
            var userData = Path.Combine(data, "LegacyIpadCharge");
            RunChargeCommand(new ChargeCommands(new LinuxIpadSystem(), userData,
                new LinuxChargeCoordination(userData), "libusb", Console.Out, Console.Error),
                command, options, stopping.Token);
        }
        else if (args[0] == "filter")
            throw new IOException("libusb-win32 filter management requires Windows.");
        else
            throw new IOException("Charging requires Windows or Linux.");
        return 0;
    }
    var format = options.GetValueOrDefault("--format", "json");
    if (format is not ("json" or "csv"))
        throw new UsageError("Use --format json or csv.");

    if (command == "monitor")
    {
        if (options.GetValueOrDefault("--source") != "ssh" ||
            !options.TryGetValue("--host", out var destination) || !BatteryTelemetry.ValidSshHost(destination))
            throw new UsageError("Use --source ssh and --host [user@]hostname or IP address.");
        var interval = Seconds(options, "--interval", 20, 1, 3600);
        var duration = Seconds(options, "--duration", 180, 1, 86400);
        if (format == "csv")
            Console.WriteLine(Diagnostics.BatteryCsvHeader);
        var identity = options.GetValueOrDefault("--identity");
        await BatteryTelemetry.Monitor(interval, duration, token => BatteryTelemetry.ReadSsh(destination, identity, token),
            sample => WriteBattery(sample, format), Console.Error, stopping.Token);
        return 0;
    }

    if (command == "report" && options.TryGetValue("--input", out var input))
    {
        string raw;
        if (input == "-")
            raw = await BatteryTelemetry.ReadBounded(Console.In, stopping.Token);
        else
        {
            try
            {
                using var reader = File.OpenText(input);
                raw = await BatteryTelemetry.ReadBounded(reader, stopping.Token);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
            {
                throw new IOException("Could not read the ioreg input file or it exceeds the input limit.");
            }
        }
        if (format == "csv")
            Console.WriteLine(Diagnostics.BatteryCsvHeader);
        WriteBattery(BatteryTelemetry.Parse(raw), format);
        return 0;
    }

    if (command == "probe" && !options.ContainsKey("--dry-run"))
        throw new UsageError("probe requires --dry-run.");
    var vid = UsbId(options.GetValueOrDefault("--vid", "05AC"));
    ushort? pid = command is "inspect" or "probe" ? UsbId(options.GetValueOrDefault("--pid", "12A2")) : null;
    UsbDevice[] devices;
    if (OperatingSystem.IsWindows())
        devices = await Task.Run(() => DeviceDiscovery.Read(vid, pid, command != "probe"), stopping.Token)
            .WaitAsync(TimeSpan.FromSeconds(15), stopping.Token);
    else if (OperatingSystem.IsLinux())
        devices = new LinuxIpadSystem().Read(vid, pid).Select(device => device.ToReport()).ToArray();
    else
        throw new IOException("USB inventory requires Windows or Linux.");
    if (command == "probe")
    {
        var capability = OperatingSystem.IsLinux() && devices.Length != 0
            ? new UsbTransportCapabilities(nameof(ChargingState.ReadOnly), null, "USB device found in sysfs. Use charge check to verify libusb access.")
            : UsbTransportCapabilities.Assess(devices);
        Diagnostics.WriteJson(Console.Out, new { dryRun = true, capability, devices });
    }
    else if (format == "csv")
        Diagnostics.WriteDevicesCsv(Console.Out, devices);
    else
        Diagnostics.WriteJson(Console.Out, devices);

    return command is "inspect" or "probe" && devices.Length == 0 ? 3 : 0;
}
catch (UsageError error)
{
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine();
    if (!CliHelp.Write(Console.Error, command ?? ""))
        CliHelp.Write(Console.Error, "");
    return 2;
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
    return 130;
}
catch (Exception error) when (error is IOException or FormatException or TimeoutException or Win32Exception or
    UnauthorizedAccessException or ArgumentException or XmlException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static void WriteBattery(BatterySample sample, string format)
{
    if (format == "csv")
        Diagnostics.WriteBatteryCsv(Console.Out, sample);
    else
        Diagnostics.WriteJson(Console.Out, sample);
}

[SupportedOSPlatform("windows")]
static void RunWindowsCommand(string command, Dictionary<string, string> options, CancellationToken token)
{
    using var identity = WindowsIdentity.GetCurrent();
    var paths = new ChargingPaths(Environment.SystemDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegacyIpadCharge"),
        Path.GetTempPath());
    var hardware = new WindowsIpadSystem(paths);
    switch (command)
    {
        case "filter install":
            var bin = Required(options, "--bin");
            RequireAdministrator(identity);
            new FilterInstallation(hardware, paths, Console.Out).Install(bin);
            return;
        case "filter remove":
            var rollback = Required(options, "--rollback-file");
            RequireAdministrator(identity);
            new FilterInstallation(hardware, paths, Console.Out).Remove(rollback,
                options.GetValueOrDefault("--recover-staged-package"));
            return;
    }

    var coordination = new WindowsChargeCoordination(paths.ChargeLock, @"Local\LegacyIpadCharge-restore-" + identity.User!.Value);
    RunChargeCommand(new ChargeCommands(hardware, paths.UserData, coordination, "libusb0", Console.Out, Console.Error),
        command, options, token);
}

static void RunChargeCommand(ChargeCommands charge, string command, Dictionary<string, string> options, CancellationToken token)
{
    switch (command)
    {
        case "charge check":
            charge.Check();
            return;
        case "charge confirm-port":
            charge.ConfirmPort(PositiveInteger(Required(options, "--max-ma"), "--max-ma"));
            return;
        case "charge trial":
            charge.Trial(CurrentLimit(options, "1000"), Seconds(options, "--seconds", 30, 1, 180), token);
            return;
        case "charge enable":
            charge.Enable(CurrentLimit(options, "2000"));
            return;
        case "charge restore":
            charge.Restore(token);
            return;
    }
}

[SupportedOSPlatform("windows")]
static void RequireAdministrator(WindowsIdentity identity)
{
    if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        throw new UnauthorizedAccessException("Open an administrator terminal to install or remove the filter.");
}

static string Required(Dictionary<string, string> options, string option) =>
    options.TryGetValue(option, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new UsageError($"Supply {option}.");

static int PositiveInteger(string value, string option) =>
    int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount > 0
        ? amount : throw new UsageError($"{option} must be a positive integer.");

static int CurrentLimit(Dictionary<string, string> options, string fallback)
{
    var limit = PositiveInteger(options.GetValueOrDefault("--limit-ma", fallback), "--limit-ma");
    return limit is 1000 or 1500 or 2000 ? limit : throw new UsageError("Use --limit-ma 1000, 1500, or 2000.");
}

static Dictionary<string, string> ParseOptions(string command, string[] args)
{
    string[] allowed = command switch
    {
        "devices" => [],
        "inspect" => ["--vid", "--pid"],
        "probe" => ["--dry-run", "--vid", "--pid"],
        "report" => ["--format", "--input"],
        "monitor" => ["--source", "--host", "--identity", "--interval", "--duration", "--format"],
        "charge check" or "charge restore" => [],
        "charge confirm-port" => ["--max-ma"],
        "charge trial" => ["--limit-ma", "--seconds"],
        "charge enable" => ["--limit-ma"],
        "filter install" => ["--bin"],
        "filter remove" => ["--rollback-file", "--recover-staged-package"],
        _ => throw new UsageError("Unknown command.")
    };
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        var option = args[i];
        if (!allowed.Contains(option) || options.ContainsKey(option))
            throw new UsageError("Unknown or repeated option.");
        if (option == "--dry-run")
            options.Add(option, "");
        else if (++i < args.Length && args[i].Length > 0 && !args[i].StartsWith("--", StringComparison.Ordinal))
            options.Add(option, args[i]);
        else
            throw new UsageError("Missing option value.");
    }
    return options;
}

static ushort UsbId(string value) =>
    value.Length == 4 && ushort.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
        out var result) ? result : throw new UsageError("Use exactly four hexadecimal digits for VID and PID.");

static TimeSpan Seconds(Dictionary<string, string> options, string name, int fallback, int min, int max)
{
    if (!options.TryGetValue(name, out var value))
        return TimeSpan.FromSeconds(fallback);
    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
        seconds < min || seconds > max)
        throw new UsageError($"{name} must be an integer between {min} and {max} seconds.");
    return TimeSpan.FromSeconds(seconds);
}

internal sealed class UsageError(string message) : Exception(message);
