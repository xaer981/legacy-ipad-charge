using System.Globalization;
using LegacyIpadCharge;

const string Usage = """
    legacy-ipad-charge devices
    legacy-ipad-charge inspect [--vid 05AC --pid 12A2]
    legacy-ipad-charge probe --dry-run [--vid 05AC --pid 12A2]
    legacy-ipad-charge report --format json|csv [--input <ioreg-file|->]
    legacy-ipad-charge monitor --source ssh --host [user@]host [--identity <file>]
                               [--interval 20 --duration 180 --format json|csv]
    """;

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    stopping.Cancel();
};
if (args is [] or ["--help"] or ["-h"])
{
    Console.WriteLine(Usage);
    return 0;
}

try
{
    var command = args[0];
    var options = ParseOptions(command, args[1..]);
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
catch (Exception error) when (error is IOException or FormatException or TimeoutException)
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

static Dictionary<string, string> ParseOptions(string command, string[] args)
{
    string[] allowed = command switch
    {
        "devices" => [],
        "inspect" => ["--vid", "--pid"],
        "probe" => ["--dry-run", "--vid", "--pid"],
        "report" => ["--format", "--input"],
        "monitor" => ["--source", "--host", "--identity", "--interval", "--duration", "--format"],
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
