using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Xml;
using LegacyIpadCharge;

const string Usage = """
    legacy-ipad-charge --version
    legacy-ipad-charge devices
    legacy-ipad-charge inspect [--vid 05AC --pid 12A2]
    legacy-ipad-charge probe --dry-run [--vid 05AC --pid 12A2]
    legacy-ipad-charge report --format json|csv [--input <ioreg-file|->]
    legacy-ipad-charge monitor --source ssh --host [user@]host [--identity <file>]
                               [--interval 20 --duration 180 --format json|csv]
    legacy-ipad-charge charge check
    legacy-ipad-charge charge confirm-port --max-ma <mA>
    legacy-ipad-charge charge trial [--limit-ma 1000] [--seconds 30]
    legacy-ipad-charge charge enable [--limit-ma 2000]
    legacy-ipad-charge charge restore
    legacy-ipad-charge filter install --bin <libusb-win32-bin-folder>
    legacy-ipad-charge filter remove --rollback-file <file> [--recover-staged-package oemNN.inf]
    """;

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
if (args is [] or ["--help"] or ["-h"] or ["charge" or "filter"] or ["charge" or "filter", "--help" or "-h"] or
    ["charge" or "filter", _, "--help" or "-h"])
{
    Console.WriteLine(Usage);
    return 0;
}

try
{
    var grouped = args[0] is "charge" or "filter";
    var command = grouped ? $"{args[0]} {args[1]}" : args[0];
    var options = ParseOptions(command, args[(grouped ? 2 : 1)..]);
    if (grouped)
    {
        if (!OperatingSystem.IsWindows())
            throw new IOException("Charging and filter management require Windows.");
        if (RuntimeInformation.OSArchitecture != Architecture.X64 || !Environment.Is64BitProcess)
            throw new IOException("Charging and filter management require Windows x64 and the x64 CLI.");
        RunWindowsCommand(command, options, stopping.Token);
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
    if (!OperatingSystem.IsWindows())
        throw new IOException("PnP inventory requires Windows.");
    var devices = await Task.Run(() => DeviceDiscovery.Read(vid, pid, command != "probe"), stopping.Token)
        .WaitAsync(TimeSpan.FromSeconds(15), stopping.Token);
    if (command == "probe")
        Diagnostics.WriteJson(Console.Out, new { dryRun = true, capability = UsbTransportCapabilities.Assess(devices), devices });
    else if (format == "csv")
        Diagnostics.WriteDevicesCsv(Console.Out, devices);
    else
        Diagnostics.WriteJson(Console.Out, devices);

    return command is "inspect" or "probe" && devices.Length == 0 ? 3 : 0;
}
catch (UsageError error)
{
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine(Usage);
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
    var charge = new ChargeCommands(hardware, paths, @"Local\LegacyIpadCharge-restore-" + identity.User!.Value,
        Console.Out, Console.Error);
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
