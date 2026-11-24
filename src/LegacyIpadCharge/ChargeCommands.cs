using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LegacyIpadCharge;

internal sealed record ChargeDevice(string InstanceId, bool Ready, string? Service, string? DriverInf,
    string[] LocationPaths, string[] UpperFilters, UsbIdentity? Usb = null);

internal static class ChargingIpad
{
    private static readonly Regex WindowsUsbPath = new(
        @"\A(?:PCIROOT|ACPI|ACPI_HAL)\([A-Z0-9_]+\)(?:#(?:ACPI|PCI)\([A-Z0-9_]+\))*#USBROOT\([0-9]+\)(?:#USB\([0-9]+\))+\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool Matches(string id) =>
        !id.Any(char.IsWhiteSpace) && Matches(UsbIdentity.Parse(id));

    internal static bool Matches(UsbIdentity? identity) => identity is
        { Vendor: 0x05AC, Interface: null, Product: 0x129A or 0x129F or 0x12A2 or 0x12A3 or
            0x12A4 or 0x12A5 or 0x12A6 or 0x12A9 or 0x12AB };

    internal static ChargeDevice Select(IEnumerable<ChargeDevice> devices)
    {
        var ipads = devices.Where(d => d.Usb is { } usb ? Matches(usb) : Matches(d.InstanceId)).ToArray();
        if (ipads is not [{ Ready: true } device])
            throw new IOException("Expected one ready iPad with a listed USB ID. Connect it alone and check the compatibility guide.");
        return device;
    }

    internal static string? WindowsPortPath(ChargeDevice device)
    {
        var locations = device.LocationPaths.Where(path => WindowsUsbPath.IsMatch(path))
            .Select(path => path.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        return locations is [var location] ? location : null;
    }
}

internal sealed record ChargingPaths(string SystemDirectory, string UserData, string Temp)
{
    internal string DriverFile => Path.Combine(SystemDirectory, "drivers", "libusb0.sys");
    internal string Journal => Path.Combine(SystemDirectory, "drivers", "LegacyIpadCharge-libusb.clixml");
    internal string ChargeLock => Path.Combine(Temp, "LegacyIpadCharge-libusb.lock");
}

internal static class LibusbFiles
{
    internal const string DriverHash = "CDA0070FE4F7C5DE5D3BB06FA6D7E29E6F6740C0F2FA319FB5C1AF235D78D616";
    internal const string InfHash = "62ABAB852A7FE61CFC9490A57908ED2793F3681575FABFB52987FEB44FD51AA3";
    internal const string ToolHash = "95F4F575639B9A4BF4F8A2F2EE226720CF87FB962AA33B6327EF0C704B63A3C4";

    internal static FileStream Lock(string path) =>
        new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);

    internal static void WriteAtomic(string path, string text)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}

internal interface IChargeConnection : IDisposable
{
    void Send(int offsetMa);
}

internal interface IChargeHardware
{
    ChargeDevice[] Devices();
    string? PortPath(ChargeDevice device);
    IChargeConnection Open(ChargeDevice device);
}

internal sealed class ChargeCommands(IChargeHardware hardware, string userData, IChargeCoordination coordination,
    string transportName, TextWriter output, TextWriter errors)
{
    internal void Check()
    {
        using var operation = coordination.Lock();
        using var handle = Open();
        output.WriteLine($"PASS {transportName} matches the connected iPad. No USB requests sent.");
    }

    internal void ConfirmPort(int maxMa)
    {
        using var operation = coordination.Lock();
        var device = ChargingIpad.Select(hardware.Devices());
        var (location, file) = PortProfile(device);
        LibusbFiles.WriteAtomic(file, maxMa.ToString(CultureInfo.InvariantCulture));
        output.WriteLine($"PORT_LIMIT_SAVED {maxMa} mA {location}");
    }

    internal void Enable(int limitMa)
    {
        using var operation = coordination.Lock();
        using var handle = OpenForCharging(limitMa);
        try { handle.Send(limitMa - 500); }
        catch
        {
            try { Restore(handle); }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
            {
                errors.WriteLine($"Restore after the failed transfer also failed. Reconnect the iPad before retrying. {error.Message}");
            }
            throw;
        }
        output.WriteLine($"ENABLE_OK {limitMa} mA {DateTimeOffset.Now:O}");
    }

    internal void Trial(int limitMa, TimeSpan duration, CancellationToken cancellationToken)
    {
        using var operation = coordination.Lock();
        using var handle = OpenForCharging(limitMa);
        using var signal = coordination.BeginTrial();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!signal.Requested)
            {
                handle.Send(limitMa - 500);
                output.WriteLine($"RAISE_OK {DateTimeOffset.Now:O}");
                signal.Wait(duration, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally { Restore(handle); }
    }

    internal void Restore(CancellationToken cancellationToken)
    {
        using var operation = coordination.LockForRestore(cancellationToken);
        using var handle = Open();
        Restore(handle);
    }

    private void Restore(IChargeConnection handle)
    {
        handle.Send(0);
        output.WriteLine($"RESTORE_OK {DateTimeOffset.Now:O}");
    }

    private IChargeConnection Open() => hardware.Open(ChargingIpad.Select(hardware.Devices()));

    private IChargeConnection OpenForCharging(int limitMa)
    {
        if (limitMa is not (1000 or 1500 or 2000))
            throw new ArgumentOutOfRangeException(nameof(limitMa), "Use a charging limit of 1000, 1500, or 2000 mA.");
        var device = ChargingIpad.Select(hardware.Devices());
        var (location, file) = PortProfile(device);
        if (!File.Exists(file))
            throw new IOException($"No confirmed current limit for {location}. Verify the rating and run charge confirm-port --max-ma <mA>.");
        if (!int.TryParse(File.ReadAllText(file).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ||
            limit < 1)
            throw new IOException($"Invalid saved current limit for {location}. Verify the rating and run charge confirm-port again.");
        if (limitMa > limit)
            throw new IOException($"Requested {limitMa} mA exceeds the confirmed limit of {limit} mA for {location}.");
        return hardware.Open(device);
    }

    private (string Location, string File) PortProfile(ChargeDevice device)
    {
        var location = hardware.PortPath(device)
            ?? throw new IOException("Cannot identify a single USB port path for the connected iPad.");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location)));
        return (location, Path.Combine(userData, "PortLimits", key + ".txt"));
    }
}
