using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LegacyIpadCharge;

internal sealed record AdapterDetails(int? VoltageMv, int? AmperageMa, int? Watts, string? Description);

internal sealed record BatterySample(DateTimeOffset? ObservedAtUtc, long? InstantAmperageMa, int? VoltageMv,
    AdapterDetails? AdapterDetails, bool? ExternalConnected, bool? IsCharging, int? CycleCount,
    int? AppleRawMaxCapacityMah, int? DesignCapacityMah, int? TemperatureRaw);

internal static class BatteryTelemetry
{
    private const string IoregCommand = "ioreg -r -l -w 0 -c AppleARMPMUCharger";
    private const int MaximumChars = 2 * 1024 * 1024;
    private static readonly Regex NodeHeader = new(
        @"^[ \t|+\-]*o[ \t]+.*<class[ \t]+(?<class>[^,>\s]+)", RegexOptions.CultureInvariant);
    private static readonly Regex PropertyLine = new(
        """^(?<prefix>[ \t|]*)"(?<key>[^"\r\n]+)"\s*=\s*(?<value>.*)$""",
        RegexOptions.CultureInvariant);
    private static readonly Regex AdapterToken = new(
        """(?:"(?:[^"\\]|\\.)*"|[{}\[\]()=,]|[^\s"{}\[\]()=,]+)""",
        RegexOptions.CultureInvariant);
    private static readonly Regex SshLabel = new(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,252}\z",
        RegexOptions.CultureInvariant);

    internal static long? ParseCurrent(string? value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed))
            return signed;
        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bits)
            ? unchecked((long)bits) : null;
    }

    internal static BatterySample Parse(string raw, DateTimeOffset? observedAtUtc = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = raw.Split('\n');
        var inCharger = !Array.Exists(lines, line => NodeHeader.IsMatch(line));
        var foundCharger = false;
        string? prefix = null;
        string? property = null;
        StringBuilder? adapter = null;
        foreach (var line in lines)
        {
            var node = NodeHeader.Match(line);
            if (node.Success)
            {
                inCharger = node.Groups["class"].Value == "AppleARMPMUCharger";
                if (inCharger && foundCharger)
                    throw new FormatException("Multiple charger readings found. Supply one snapshot.");
                foundCharger |= inCharger;
                continue;
            }
            if (!inCharger)
                continue;

            var match = PropertyLine.Match(line.TrimEnd('\r'));
            if (match.Success)
                prefix ??= match.Groups["prefix"].Value;
            if (match.Success && match.Groups["prefix"].Value == prefix)
            {
                property = match.Groups["key"].Value;
                var value = match.Groups["value"].Value.Trim();
                if (property is "AdapterDetails")
                {
                    if (adapter is not null)
                        throw new FormatException("Multiple AdapterDetails properties found.");
                    adapter = new StringBuilder(value);
                }
                else if (property is "InstantAmperage" or "Voltage" or "ExternalConnected" or "IsCharging" or
                    "CycleCount" or "AppleRawMaxCapacity" or "DesignCapacity" or "Temperature")
                {
                    if (!values.TryAdd(property, value))
                        throw new FormatException("Multiple charger readings found. Supply one snapshot.");
                }
            }
            else if (property == "AdapterDetails")
            {
                adapter!.Append('\n').Append(line.TrimStart(' ', '\t', '|'));
            }
        }

        var sample = new BatterySample(observedAtUtc, ParseCurrent(values.GetValueOrDefault("InstantAmperage")),
            Number(values, "Voltage"), adapter is null ? null : ParseAdapter(adapter.ToString()),
            Flag(values.GetValueOrDefault("ExternalConnected")), Flag(values.GetValueOrDefault("IsCharging")),
            Number(values, "CycleCount"), Number(values, "AppleRawMaxCapacity"),
            Number(values, "DesignCapacity"), Number(values, "Temperature"));
        if (sample is { InstantAmperageMa: null, VoltageMv: null, ExternalConnected: null,
                IsCharging: null, CycleCount: null, AppleRawMaxCapacityMah: null, DesignCapacityMah: null,
                TemperatureRaw: null, AdapterDetails: null or
                { VoltageMv: null, AmperageMa: null, Watts: null, Description: null } })
            throw new FormatException("No usable AppleARMPMUCharger properties found.");
        return sample;
    }

    private static AdapterDetails? ParseAdapter(string text)
    {
        var tokens = AdapterToken.Matches(text);
        if (tokens.Count == 0 || tokens[0].Value != "{")
            return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var depth = 1;
        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i].Value;
            if (token is "{" or "[" or "(")
                depth++;
            if (token is "}" or "]" or ")")
                depth--;
            if (depth == 0)
                return new AdapterDetails(Number(values, "Voltage") ?? Number(values, "AdapterVoltage"),
                    Number(values, "Amperage"),
                    Number(values, "Watts"), values.GetValueOrDefault("Description") switch
                    {
                        "\"usb host\"" => "usb host",
                        "\"usb brick\"" => "usb brick",
                        _ => null
                    });
            if (depth == 1 && i + 2 < tokens.Count && tokens[i + 1].Value == "=" &&
                token is "\"Voltage\"" or "\"AdapterVoltage\"" or "\"Amperage\"" or "\"Watts\"" or "\"Description\"")
                values.TryAdd(token[1..^1], tokens[i + 2].Value);
        }
        return null;
    }

    private static int? Number(Dictionary<string, string> values, string key) =>
        int.TryParse(values.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var number) ? number : null;

    private static bool? Flag(string? value) => value?.ToUpperInvariant() switch
    {
        "YES" or "TRUE" or "1" => true,
        "NO" or "FALSE" or "0" => false,
        _ => null
    };

    internal static bool ValidSshHost(string destination)
    {
        var parts = destination.Split('@');
        if (parts.Length > 2 || (parts.Length == 2 && !SshLabel.IsMatch(parts[0])))
            return false;
        var host = parts[^1];
        return SshLabel.IsMatch(host) || (host.Contains(':') && IPAddress.TryParse(host, out _));
    }

    internal static async Task Monitor(TimeSpan interval, TimeSpan duration,
        Func<CancellationToken, Task<BatterySample>> read, Action<BatterySample> write, TextWriter errors,
        CancellationToken stopping)
    {
        var clock = Stopwatch.StartNew();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        session.CancelAfter(duration);
        var count = 0;
        try
        {
            while (!session.IsCancellationRequested && clock.Elapsed < duration)
            {
                var nextSample = clock.Elapsed + interval;
                BatterySample? sample = null;
                try
                {
                    sample = await read(session.Token);
                }
                catch (Exception error) when (error is IOException or FormatException or TimeoutException)
                {
                    errors.WriteLine($"{DateTimeOffset.UtcNow:O} Skipped SSH sample. {error.Message}");
                }

                if (sample is not null)
                {
                    write(sample);
                    count++;
                }

                var delay = nextSample - clock.Elapsed;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, session.Token);
            }
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested && !stopping.IsCancellationRequested)
        {
            // The recording deadline can arrive during an individual read.
        }

        stopping.ThrowIfCancellationRequested();
        if (count == 0)
            throw new TimeoutException("No complete SSH sample within the requested duration.");
    }

    internal static async Task<BatterySample> ReadSsh(string destination, string? identity,
        CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows())
            throw new IOException("Live SSH sampling uses Windows OpenSSH.");
        if (!ValidSshHost(destination))
            throw new ArgumentException("Use a hostname or IP, optionally prefixed with user@.");

        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh.exe"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        string[] sshOptions = ["-n", "-T", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes",
            "-o", "UpdateHostKeys=no", "-o", "ConnectTimeout=5", "-o", "ConnectionAttempts=1",
            "-o", "PreferredAuthentications=publickey", "-o", "PermitLocalCommand=no", "-o", "ControlPath=none",
            "-o", "ClearAllForwardings=yes", "-o", "ForwardAgent=no", "-o", "ForwardX11=no"];
        foreach (var option in sshOptions)
            start.ArgumentList.Add(option);
        if (identity is not null)
        {
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add("IdentitiesOnly=yes");
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(identity);
        }
        foreach (var argument in new[] { "--", destination, IoregCommand })
            start.ArgumentList.Add(argument);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        Process? ssh;
        try
        {
            ssh = Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new IOException("Could not start Windows OpenSSH.");
        }
        using var process = ssh ?? throw new IOException("Could not start Windows OpenSSH.");
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
        try
        {
            var raw = await ReadBounded(process.StandardOutput, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            if (process.ExitCode != 0)
                throw new IOException($"SSH/ioreg returned exit code {process.ExitCode}.");
            return Parse(raw, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("SSH/ioreg did not finish within 10 seconds.");
        }
        finally
        {
            deadline.Cancel();
            if (!process.HasExited)
                process.Kill();
            try { await stderr; }
            catch (OperationCanceledException) { }
        }
    }

    internal static async Task<string> ReadBounded(TextReader reader, CancellationToken cancellation)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int length;
        while ((length = await reader.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
        {
            if (result.Length + length > MaximumChars)
                throw new IOException("ioreg input exceeds the 2 Mi-character limit.");
            result.Append(buffer, 0, length);
        }
        return result.ToString();
    }
}
