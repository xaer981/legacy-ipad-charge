using System.Globalization;
using System.Text.RegularExpressions;

namespace LegacyIpadCharge;

internal sealed record LinuxUsbDevice(string Name, UsbIdentity Identity, byte Bus, byte Address, int[] Ports,
    string? PortPath, bool Ready)
{
    internal string InstanceId => $"{Name}/{Address}";

    internal ChargeDevice ToChargeDevice() => new(InstanceId, Ready, null, null,
        PortPath is null ? [] : [PortPath], [], Identity);

    internal UsbDevice ToReport() => new(Identity.Vendor.ToString("X4"), Identity.Product.ToString("X4"),
        Identity is { Vendor: 0x05AC, Product: 0x12A2 } ? "iPad 2 (GSM)" : null,
        PortPath, Ports, null, null, null, new("physical", null, null, [], [], [], [], null, Ready, null), []);
}

internal sealed class LinuxIpadSystem(string sysfs = "/sys/bus/usb/devices") : IChargeHardware
{
    private static readonly Regex DeviceName = new(@"\A[0-9]+-(?<ports>[0-9]+(?:\.[0-9]+)*)\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex RootName = new(@"\Ausb[0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly Regex PciAddress = new(@"\A[0-9a-f]{4}:[0-9a-f]{2}:[0-9a-f]{2}\.[0-7]\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex UsbVersion = new(@"\A[123]\.[0-9]{2}\z", RegexOptions.CultureInvariant);

    public ChargeDevice[] Devices() => Read(0x05AC).Where(device => ChargingIpad.Matches(device.Identity))
        .Select(device => device.ToChargeDevice()).ToArray();

    public string? PortPath(ChargeDevice device) => device.LocationPaths is [var path] ? path : null;

    public IChargeConnection Open(ChargeDevice device)
    {
        var current = Read(0x05AC).Where(candidate => ChargingIpad.Matches(candidate.Identity)).ToArray();
        if (current is not [{ Ready: true } selected] || selected.InstanceId != device.InstanceId ||
            selected.Identity != device.Usb || selected.PortPath != PortPath(device))
            throw new IOException("The selected iPad or its USB path changed. Reconnect and retry.");

        return LinuxLibusbConnection.Open(selected);
    }

    internal LinuxUsbDevice[] Read(ushort vendor, ushort? product = null)
    {
        string[] entries;
        try { entries = Directory.GetDirectories(sysfs); }
        catch (DirectoryNotFoundException) { return []; }
        var roots = entries.Where(path => RootName.IsMatch(Path.GetFileName(path)))
            .Select(path => (Name: Path.GetFileName(path), Key: RootKey(path)))
            .Where(root => root.Key is not null)
            .GroupBy(root => root.Key!)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Single().Name, group => group.Key);

        var devices = new List<LinuxUsbDevice>();
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            var match = DeviceName.Match(name);
            if (!match.Success || !Hex(entry, "idVendor", out var vid) || vid != vendor ||
                !Hex(entry, "idProduct", out var pid) || (product is { } requested && pid != requested) ||
                !Number(entry, "busnum", out var bus) || !Number(entry, "devnum", out var address))
                continue;

            var portText = match.Groups["ports"].Value;
            var ports = portText.Split('.').Select(port => int.Parse(port, CultureInfo.InvariantCulture)).ToArray();
            var rootName = "usb" + bus.ToString(CultureInfo.InvariantCulture);
            // A USB/IP controller has no locally verifiable physical port.
            var portPath = roots.TryGetValue(rootName, out var root) ? $"{root}:{portText}" : null;
            var authorized = Attribute(entry, "authorized");
            if (authorized is not null)
                devices.Add(new LinuxUsbDevice(name, new UsbIdentity(vid, pid, null), bus, address, ports,
                    portPath, authorized == "1"));
        }
        return devices.OrderBy(device => device.Bus).ThenBy(device => device.Address).ToArray();
    }

    private static string? RootKey(string root)
    {
        var target = new DirectoryInfo(root).ResolveLinkTarget(returnFinalTarget: true);
        if (target is null || !target.Exists || target is not DirectoryInfo { Parent: { } controller } ||
            !PciAddress.IsMatch(controller.Name) || Attribute(root, "version") is not { } version ||
            !UsbVersion.IsMatch(version))
            return null;
        return $"linux:pci:{controller.Name}:usb{version}";
    }

    private static bool Hex(string path, string name, out ushort value) =>
        ushort.TryParse(Attribute(path, name), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);

    private static bool Number(string path, string name, out byte value) =>
        byte.TryParse(Attribute(path, name), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;

    private static string? Attribute(string path, string name)
    {
        try { return File.ReadAllText(Path.Combine(path, name)).Trim(); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
