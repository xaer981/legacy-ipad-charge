using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LegacyIpadCharge;

[SupportedOSPlatform("windows")]
internal sealed class WindowsIpadSystem(ChargingPaths paths) : IChargeHardware, IFilterSystem
{
    private static readonly Guid DriverProperties = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6");
    private readonly string windows = Path.GetDirectoryName(paths.SystemDirectory)!;

    public ChargeDevice[] Devices() => ConfigurationManager.UsbInstanceIds()
        .Where(ChargingIpad.Matches)
        .Select(id => (Id: id, Node: ConfigurationManager.Locate(id)))
        .Where(item => item.Node.HasValue)
        .Select(item => ReadDevice(item.Id, item.Node!.Value))
        .ToArray();

    private static ChargeDevice ReadDevice(string id, uint node)
    {
        var (started, problem) = ConfigurationManager.Status(node);
        return new ChargeDevice(id, started is true && problem is null,
            ConfigurationManager.StringProperty(node, DeviceDiscovery.DeviceProperties, 6),
            ConfigurationManager.StringProperty(node, DriverProperties, 5),
            ConfigurationManager.StringsProperty(node, DeviceDiscovery.DeviceProperties, 37),
            ConfigurationManager.StringsProperty(node, DeviceDiscovery.DeviceProperties, 19));
    }

    public string? PortPath(ChargeDevice device) => ChargingIpad.WindowsPortPath(device);

    public IChargeConnection Open(ChargeDevice device)
    {
        var driver = Service("libusb0");
        if (driver is not { Running: true } || !FilterInstallation.Same(driver.Path, paths.DriverFile) ||
            !MatchesHash(paths.DriverFile, LibusbFiles.DriverHash))
            throw new IOException("Expected the running libusb-win32 1.4.0.2 x64 filter from the installation guide.");
        if (!FilterInstallation.Same(device.Service, "usbccgp") || !HasLibusb(device.UpperFilters))
            throw new IOException("Expected libusb0 above the Apple composite USB driver. Reconnect the iPad and retry.");
        return LibusbConnection.Open(device.InstanceId);
    }

    public DriverPackage[] Packages()
    {
        var packages = new List<DriverPackage>();
        var store = Path.Combine(paths.SystemDirectory, "DriverStore", "FileRepository");
        foreach (var directory in Directory.EnumerateDirectories(store, "libusb0.inf_*"))
        {
            var inf = Path.Combine(directory, "libusb0.inf");
            var target = new StringBuilder(260);
            if (!SetupGetInfPublishedNameW(inf, target, (uint)target.Capacity, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resolve the published INF for a libusb0 package.");
            packages.Add(new DriverPackage(Path.GetFileName(target.ToString()), inf));
        }
        return packages.ToArray();
    }

    public DriverService? Service(string name)
    {
        using var manager = OpenSCManagerW(null, null, 1);
        if (manager.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Opening Windows Service Control Manager failed.");
        using var service = OpenServiceW(manager, name, 4);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1060)
                return null;
            throw new Win32Exception(error, $"Opening service {name} failed.");
        }
        if (!QueryServiceStatus(service, out var status))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Querying service {name} failed.");
        using var config = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        var image = config?.GetValue("ImagePath") as string;
        return new DriverService(NormalizeDriverPath(image), status.CurrentState);
    }

    private string? NormalizeDriverPath(string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return null;
        var path = image.Trim().Trim('"');
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(windows, path[12..]);
        if (path.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            path = path[4..];
        if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(windows, path);
        return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null;
    }

    public bool HasClassFilter()
    {
        using var classes = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class")
            ?? throw new IOException("Could not read Windows device class filters.");
        foreach (var name in classes.GetSubKeyNames())
        {
            using var type = classes.OpenSubKey(name);
            if (HasLibusb(type?.GetValue("UpperFilters") as string[] ?? []) ||
                HasLibusb(type?.GetValue("LowerFilters") as string[] ?? []))
                return true;
        }
        return false;
    }

    public FilterBinding[] FilterBindings()
    {
        var bindings = new List<FilterBinding>();
        // A disconnected device may still have a persisted per-device filter.
        foreach (var id in ConfigurationManager.UsbInstanceIds(presentOnly: false))
        {
            if (ConfigurationManager.Locate(id, includePhantom: true) is not { } node)
                continue;
            if (HasLibusb(ConfigurationManager.StringsProperty(node, DeviceDiscovery.DeviceProperties, 19)))
                bindings.Add(new FilterBinding(id, FilterBindingKind.Upper));
            if (HasLibusb(ConfigurationManager.StringsProperty(node, DeviceDiscovery.DeviceProperties, 20)))
                bindings.Add(new FilterBinding(id, FilterBindingKind.Lower));
            if (FilterInstallation.Same(ConfigurationManager.StringProperty(node, DeviceDiscovery.DeviceProperties, 6), "libusb0"))
                bindings.Add(new FilterBinding(id, FilterBindingKind.Service));
            if (HasLibusb(ConfigurationManager.StringsProperty(node, DeviceDiscovery.StackProperties, 14)))
                bindings.Add(new FilterBinding(id, FilterBindingKind.Stack));
        }
        return bindings.ToArray();
    }

    private static bool HasLibusb(string[] filters) => filters.Any(filter =>
        FilterInstallation.Same(filter, "libusb0") || FilterInstallation.Same(filter, @"\Driver\libusb0"));

    public bool MatchesHash(string path, string expected)
    {
        try
        {
            using var file = File.OpenRead(path);
            return FilterInstallation.Same(Convert.ToHexString(SHA256.HashData(file)), expected);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public int Run(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = paths.SystemDirectory };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException($"Could not start {Path.GetFileName(executable)}.");
        process.WaitForExit();
        return process.ExitCode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ServiceStatus(uint ServiceType, uint CurrentState, uint ControlsAccepted,
        uint Win32ExitCode, uint ServiceSpecificExitCode, uint CheckPoint, uint WaitHint);

    private sealed class ServiceHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern ServiceHandle OpenSCManagerW(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern ServiceHandle OpenServiceW(ServiceHandle manager, string name, uint access);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(ServiceHandle service, out ServiceStatus status);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr service);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupGetInfPublishedNameW(string storeInf, StringBuilder output, uint size,
        out uint requiredSize);
}
