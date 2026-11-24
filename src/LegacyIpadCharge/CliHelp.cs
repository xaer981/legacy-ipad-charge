namespace LegacyIpadCharge;

internal static class CliHelp
{
    private sealed record Entry(string Summary, string Arguments = "", string Details = "", string? Example = null);

    private static readonly Dictionary<string, Entry> Commands = new(StringComparer.Ordinal)
    {
        ["devices"] = new("List connected Apple USB devices and their vendor and product IDs.",
            Details: "Prints read-only USB inventory as JSON. Use the product ID with inspect."),
        ["inspect"] = new("Show USB details for a device matching a vendor and product ID.",
            "[--vid <hex>] [--pid <hex>]",
            "Use four hexadecimal digits for each ID. Defaults: --vid 05AC --pid 12A2 (iPad 2 GSM).",
            "inspect --pid 12A2"),
        ["probe"] = new("Check what USB access is visible in the system inventory.",
            "--dry-run [--vid <hex>] [--pid <hex>]",
            "--dry-run is required. Defaults: --vid 05AC --pid 12A2. Use charge check to test access to the charging transport.",
            "probe --dry-run --pid 12A2"),
        ["report"] = new("Export USB inventory or parse a saved iPad battery snapshot.",
            "[--format json|csv] [--input <file|->]",
            """
            --format   Output format (default: json).
            --input    Read an ioreg snapshot from a UTF-8 file or use - for stdin.
                       Omit this option to export USB inventory instead.
            """, "report --format csv --input charger.txt"),
        ["monitor"] = new("Sample iPad battery telemetry over an existing SSH connection.",
            "--source ssh --host <[user@]host> [--identity <file>]\n" +
            "[--interval <seconds>] [--duration <seconds>] [--format json|csv]",
            """
            Requires OpenSSH, an iPad with ioreg access, key authentication, and a verified host key.
            --host       SSH host, address, or alias.
            --identity   Optional SSH private key file.
            --interval   Seconds between samples (1-3600, default: 20).
            --duration   Total seconds to sample (1-86400, default: 180).
            --format     Output format (default: one JSON object per line).
            """, "monitor --source ssh --host ipad --format csv"),
        ["charge check"] = new("Check access to one supported iPad without sending a charging request.",
            Details: "Run this after setup or reconnecting the iPad. PASS confirms the CLI can open the charging transport."),
        ["charge confirm-port"] = new("Save the verified current rating for the connected USB path.",
            "--max-ma <mA>",
            """
            --max-ma   Enter a positive whole number in milliamps. Use the lowest confirmed
                       rating of the port, cable, and any adapter at 5 V with USB data.
            The CLI saves your input for this USB path and user account. Check it again if the cable or adapter changes.
            """, "charge confirm-port --max-ma 1000"),
        ["charge trial"] = new("Try a higher current for a limited time, then request the 500 mA baseline.",
            "[--limit-ma <mA>] [--seconds <seconds>]",
            """
            --limit-ma   Request 1000, 1500, or 2000 mA (default: 1000).
            --seconds    Trial duration (1-180, default: 30).
            Save a sufficient rating with charge confirm-port first. Check USB data and charging during the trial.
            Ctrl+C also triggers a restore attempt. RESTORE_OK confirms that the restore request completed.
            """, "charge trial --limit-ma 1000 --seconds 30"),
        ["charge enable"] = new("Request a higher current for daily use, without a timer.",
            "[--limit-ma <mA>]",
            """
            --limit-ma   Request 1000, 1500, or 2000 mA (default: 2000).
            Save a sufficient rating with charge confirm-port first. Run charge restore to end the session.
            After reconnecting, run charge check before enabling again.
            """, "charge enable --limit-ma 1500"),
        ["charge restore"] = new("Request the 500 mA baseline.",
            Details: "Works without a saved port rating. It can also stop an active trial started by the same user."),
        ["filter install"] = new("Install the signed libusb-win32 filter for the connected iPad.",
            "--bin <folder>",
            """
            Windows x64 only. Run from an administrator terminal.
            --bin   Path to the bin folder in the extracted libusb-win32 1.4.0.2 package.
            Keep the rollback file printed by the installer. Reconnect the iPad and run charge check.
            """, "filter install --bin \"C:\\path\\to\\libusb-win32\\bin\""),
        ["filter remove"] = new("Remove the installed filter using its rollback file.",
            "--rollback-file <file> [--recover-staged-package <oemNN.inf>]",
            """
            Windows x64 only. Run charge restore first, then remove from an administrator terminal.
            --rollback-file          Path to the backup printed by filter install.
            --recover-staged-package Use only for interrupted or older installations. Supply the verified published INF name.
            If prompted, reconnect the iPad and run the same removal command again.
            """, "filter remove --rollback-file \"C:\\path\\to\\libusb-rollback.clixml\"")
    };

    private const string WindowsExecutable = @".\legacy-ipad-charge.exe";

    internal static bool Write(TextWriter output, string topic)
    {
        var executable = OperatingSystem.IsWindows() || topic == "filter" || topic.StartsWith("filter ", StringComparison.Ordinal)
            ? WindowsExecutable : "./legacy-ipad-charge";
        switch (topic)
        {
            case "":
                output.WriteLine("Legacy iPad USB Charging");
                output.WriteLine("Request more charging current for a supported iPad while keeping its USB data connection.");
                output.WriteLine($"\nUsage\n  {executable} <command> [options]");
                WriteList(output, "Charging", command => command.StartsWith("charge ", StringComparison.Ordinal));
                WriteList(output, "Diagnostics and battery telemetry", command => !command.Contains(' '));
                if (OperatingSystem.IsWindows())
                    WriteList(output, "Windows filter setup", command => command.StartsWith("filter ", StringComparison.Ordinal));
                output.WriteLine();
                output.WriteLine("  --version              Show the installed CLI version.");
                output.WriteLine("  --help, -h             Show help for the CLI or a command.");
                output.WriteLine($"\nGetting started\n  {executable} devices\n  {executable} charge trial --help");
                return true;
            case "charge":
                WriteList(output, "Charging", command => command.StartsWith("charge ", StringComparison.Ordinal));
                output.WriteLine($"\nCommand help\n  {executable} charge <action> --help");
                return true;
            case "filter":
                WriteList(output, "Windows x64 filter setup", command => command.StartsWith("filter ", StringComparison.Ordinal));
                output.WriteLine($"\nCommand help\n  {executable} filter <action> --help");
                return true;
        }

        if (!Commands.TryGetValue(topic, out var entry))
            return false;

        var usage = $"  {executable} {topic}";
        if (entry.Arguments.Length != 0)
            usage += " " + entry.Arguments.Replace("\n", "\n" + new string(' ', usage.Length + 1));
        output.WriteLine($"Usage\n{usage}");
        output.WriteLine($"\n{entry.Summary}");
        if (entry.Details.Length != 0)
            output.WriteLine($"\n{entry.Details}");
        if (entry.Example is not null)
            output.WriteLine($"\nExample\n  {executable} {entry.Example}");
        return true;
    }

    private static void WriteList(TextWriter output, string heading, Func<string, bool> includes)
    {
        output.WriteLine($"\n{heading}");
        foreach (var (command, entry) in Commands)
        {
            if (includes(command))
                output.WriteLine($"  {command,-23}{entry.Summary}");
        }
    }
}
