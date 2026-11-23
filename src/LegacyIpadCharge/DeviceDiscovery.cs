using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace LegacyIpadCharge;

internal readonly record struct UsbIdentity(ushort Vendor, ushort Product, byte? Interface)
{
    private static readonly Regex InstancePrefix = new(
        @"^USB\\VID_([0-9A-F]{4})&PID_([0-9A-F]{4})(?:&MI_([0-9A-F]{2}))?\\[^\\]+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static UsbIdentity? Parse(string instanceId)
    {
        var match = InstancePrefix.Match(instanceId);
        if (!match.Success)
            return null;
        return new UsbIdentity(
            ushort.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            ushort.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            match.Groups[3].Success
                ? byte.Parse(match.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : null);
    }
}

internal sealed record DeviceNode(string Kind, int? InterfaceNumber, string? Service,
    string[] UpperFilters, string[] LowerFilters, string[] DriverStack, Guid[] InterfaceClassGuids,
    bool? WinRtUsbRestricted, bool? Started, uint? ProblemCode);

internal sealed record UsbDevice(string Vid, string Pid, string? ModelFromUsbId, string? LocationPath,
    int[] PortChain, string? ParentHubUsbId, HubConnection? Connection, string? ConnectionStatus,
    DeviceNode Device, DeviceNode[] Functions);

internal static class DeviceDiscovery
{
    private static readonly Guid UsbHubClass = new("f18a0e88-c30c-11d0-8815-00a0c906bed8");
    private static readonly Guid WinRtUsbClass = new("dee824ef-729b-4a0e-9c14-b7117d33a817");
    private static readonly Guid DeviceProperties = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    private static readonly Guid TopologyProperties = new("4340a6c5-93fa-4706-972c-7b648008a5a7");
    private static readonly Guid StackProperties = new("540b947e-8b40-45bc-a8a2-6a0b894cbda2");
    private static readonly Regex UsbLocation = new(
        @"^(?:PCIROOT|ACPI|ACPI_HAL)\([A-Z0-9_]+\)(?:#(?:ACPI|PCI)\([A-Z0-9_]+\))*#USBROOT\(\d+\)(?:#USB\(\d+\))+$",
        RegexOptions.CultureInvariant);

    internal static UsbDevice[] Read(ushort vid, ushort? pid, bool queryHub)
    {
        var nodes = ConfigurationManager.UsbInstanceIds()
            .Select(id => (Id: id, Identity: UsbIdentity.Parse(id)))
            .Where(node => node.Identity is { } usb && usb.Vendor == vid &&
                (pid is null || usb.Product == pid))
            .Select(node => (node.Id, Identity: node.Identity!.Value,
                DevInst: ConfigurationManager.Locate(node.Id)))
            .Where(node => node.DevInst.HasValue)
            .Select(node => (node.Id, node.Identity, DevInst: node.DevInst!.Value,
                Parent: ConfigurationManager.StringProperty(node.DevInst.Value, TopologyProperties, 8)))
            .ToArray();
        if (nodes.Length == 0)
            return [];

        var classes = ConfigurationManager.InterfaceClasses();
        return nodes.Where(node => node.Identity.Interface is null)
            .Select(device =>
            {
                var location = ConfigurationManager.StringsProperty(device.DevInst, DeviceProperties, 37)
                    .FirstOrDefault(path => UsbLocation.IsMatch(path));
                int[] ports = location is null ? [] : Regex.Matches(location, @"#USB\((\d+)\)")
                    .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
                var parentUsb = device.Parent is null ? null : UsbIdentity.Parse(device.Parent);
                var hub = queryHub && ports.Length > 0 && device.Parent is not null
                    ? ConfigurationManager.InterfacePaths(UsbHubClass, device.Parent).FirstOrDefault()
                    : null;
                var (connection, status) = hub is null
                    ? (null, queryHub ? "hub-or-port-unavailable" : "not-queried")
                    : UsbHub.Read(hub, ports[^1], device.Identity);
                return new UsbDevice(
                    $"{device.Identity.Vendor:X4}", $"{device.Identity.Product:X4}",
                    device.Identity is { Vendor: 0x05AC, Product: 0x12A2 } ? "iPad 2 (GSM)" : null,
                    location, ports, parentUsb is { } parent ? $"{parent.Vendor:X4}:{parent.Product:X4}" : null,
                    connection, status,
                    ReadNode(device.Id, device.DevInst, null, classes),
                    nodes.Where(child => child.Identity.Interface.HasValue &&
                            string.Equals(child.Parent, device.Id, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(child => child.Identity.Interface)
                        .Select(child => ReadNode(child.Id, child.DevInst, child.Identity.Interface, classes))
                        .ToArray());
            })
            .OrderBy(device => device.LocationPath, StringComparer.Ordinal)
            .ThenBy(device => device.Pid, StringComparer.Ordinal)
            .ToArray();
    }

    private static DeviceNode ReadNode(string id, uint devInst, byte? number, Guid[] classes)
    {
        var (started, problem) = ConfigurationManager.Status(devInst);
        var presentClasses = classes.Where(guid => ConfigurationManager.HasInterface(guid, id)).ToArray();
        var winRtPaths = presentClasses.Contains(WinRtUsbClass)
            ? ConfigurationManager.InterfacePaths(WinRtUsbClass, id) : [];
        return new DeviceNode(number is null ? "physical" : "function", number,
            DriverName(ConfigurationManager.StringProperty(devInst, DeviceProperties, 6)),
            DriverNames(ConfigurationManager.StringsProperty(devInst, DeviceProperties, 19)),
            DriverNames(ConfigurationManager.StringsProperty(devInst, DeviceProperties, 20)),
            DriverNames(ConfigurationManager.StringsProperty(devInst, StackProperties, 14)),
            presentClasses,
            winRtPaths.Length == 1 ? ConfigurationManager.InterfaceRestricted(winRtPaths[0]) : null,
            started, problem);
    }

    private static string? DriverName(string? name)
    {
        if (name?.StartsWith(@"\Driver\", StringComparison.OrdinalIgnoreCase) is true)
            name = name[8..];
        return name is not null && Regex.IsMatch(name, @"\A[A-Za-z0-9_. -]{1,128}\z")
            ? name : null;
    }

    private static string[] DriverNames(string[] names) =>
        names.Select(DriverName).OfType<string>().ToArray();

}

internal static class ConfigurationManager
{
    private const uint PresentUsbEnumerator = 0x101;
    private const uint PresentInterfaces = 0;
    private const uint EnumerateInterfaceClasses = 1;
    private const uint BufferSmall = 0x1A;
    private const uint NoSuchValue = 0x25;
    private const uint NoSuchDevNode = 0x0D;
    private const uint StringType = 0x12;
    private const uint StringListType = 0x2012;
    private const uint BooleanType = 0x11;
    private const uint DeviceStarted = 0x08;
    private const uint DeviceHasProblem = 0x400;
    private const string Library = "cfgmgr32.dll";

    internal static string[] UsbInstanceIds()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Check(CM_Get_Device_ID_List_SizeW(out var size, "USB", PresentUsbEnumerator));
            var buffer = new char[size];
            var result = CM_Get_Device_ID_ListW("USB", buffer, size, PresentUsbEnumerator);
            if (result == BufferSmall)
                continue;
            Check(result);
            return MultiString(new string(buffer));
        }
        throw new IOException("PnP inventory changed during all three enumeration attempts.");
    }

    internal static uint? Locate(string id)
    {
        var result = CM_Locate_DevNodeW(out var devInst, id, 0);
        if (result == NoSuchDevNode)
            return null;
        Check(result);
        return devInst;
    }

    internal static (bool? Started, uint? Problem) Status(uint instance)
    {
        if (CM_Get_DevNode_Status(out var status, out var problem, instance, 0) != 0)
            return (null, null);
        return ((status & DeviceStarted) != 0, (status & DeviceHasProblem) != 0 ? problem : null);
    }

    internal static string? StringProperty(uint instance, Guid guid, uint property) =>
        ReadProperty(instance, new PropertyKey(guid, property), StringType)?.TrimEnd('\0');

    internal static string[] StringsProperty(uint instance, Guid guid, uint property) =>
        MultiString(ReadProperty(instance, new PropertyKey(guid, property), StringListType) ?? "");

    private static string? ReadProperty(uint instance, PropertyKey key, uint expectedType)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint size = 0;
            if (CM_Get_DevNode_PropertyW(instance, in key, out _, null, ref size, 0) != BufferSmall ||
                size == 0 || size > 65536)
                return null;
            var bytes = new byte[size];
            var result = CM_Get_DevNode_PropertyW(instance, in key, out var type, bytes, ref size, 0);
            if (result == BufferSmall)
                continue;
            return result == 0 && type == expectedType
                ? Encoding.Unicode.GetString(bytes, 0, (int)size) : null;
        }
        return null;
    }

    internal static Guid[] InterfaceClasses()
    {
        var classes = new List<Guid>();
        for (uint index = 0; ; index++)
        {
            var result = CM_Enumerate_Classes(index, out var guid, EnumerateInterfaceClasses);
            if (result == NoSuchValue)
                return classes.ToArray();
            Check(result);
            classes.Add(guid);
        }
    }

    internal static bool HasInterface(Guid guid, string id) =>
        CM_Get_Device_Interface_List_SizeW(out var size, in guid, id, PresentInterfaces) == 0 && size > 1;

    internal static string[] InterfacePaths(Guid guid, string id)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_Interface_List_SizeW(out var size, in guid, id, PresentInterfaces) != 0 ||
                size <= 1)
                return [];
            var buffer = new char[size];
            var result = CM_Get_Device_Interface_ListW(in guid, id, buffer, size, PresentInterfaces);
            if (result == BufferSmall)
                continue;
            return result == 0 ? MultiString(new string(buffer)) : [];
        }
        return [];
    }

    internal static bool? InterfaceRestricted(string path)
    {
        var key = new PropertyKey(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 6);
        byte[] buffer = new byte[1];
        uint size = 1;
        if (CM_Get_Device_Interface_PropertyW(path, in key, out var type, buffer, ref size, 0) != 0 ||
            type != BooleanType || size != 1)
            return null;
        return buffer[0] != 0;
    }

    private static string[] MultiString(string value) =>
        value.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static void Check(uint result)
    {
        if (result != 0)
            throw new IOException($"Configuration Manager failed (CONFIGRET 0x{result:X}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyKey(Guid Format, uint Property);

    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);
    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint length, uint flags);
    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint instance, string id, uint flags);
    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_PropertyW(uint instance, in PropertyKey key, out uint type,
        [Out] byte[]? buffer, ref uint size, uint flags);
    [DllImport(Library, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint instance, uint flags);
    [DllImport(Library, ExactSpelling = true)]
    private static extern uint CM_Enumerate_Classes(uint index, out Guid guid, uint flags);
    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(out uint length, in Guid guid, string id, uint flags);
    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_ListW(in Guid guid, string id, [Out] char[] buffer,
        uint length, uint flags);
    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_PropertyW(string path, in PropertyKey key, out uint type,
        [Out] byte[] buffer, ref uint size, uint flags);
}
