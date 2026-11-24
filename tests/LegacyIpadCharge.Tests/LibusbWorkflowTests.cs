using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LegacyIpadCharge;

internal static class LibusbWorkflowTests
{
    private const string Ipad = @"USB\VID_05AC&PID_12A2\MOCK";
    private const string Candidate = @"USB\VID_05AC&PID_12AB\MOCK";
    private const string FirstPort = "PCIROOT(80)#PCI(1400)#USBROOT(0)#USB(2)";
    private const string SecondPort = FirstPort + "#USB(1)";

    internal static void RequestsAndDevices()
    {
        const string prefix = "B80B000002000000000000004000000000000000";
        foreach (var (offset, suffix) in new[] { (0, "00000000"), (500, "F4010000"), (1000, "E8030000"), (1500, "DC050000") })
        {
            Check(Convert.ToHexString(LibusbConnection.Request(offset)) == prefix + suffix);
            Check(Convert.ToHexString(AppleChargingProtocol.ActiveCurrent(offset).ToBytes()) == "40400000" + suffix);
        }

        var buffer = new byte[256];
        var bytes = Encoding.ASCII.GetBytes(@"\Registry\Machine\System\CurrentControlSet\Enum\" + Ipad + "\\Device Parameters\0");
        bytes.CopyTo(buffer, 0);
        Check(LibusbConnection.MatchesInstance(buffer, (uint)bytes.Length, Ipad.ToLowerInvariant()));
        Check(!LibusbConnection.MatchesInstance(buffer, (uint)bytes.Length, Candidate));
        Check(!LibusbConnection.MatchesInstance(buffer, (uint)bytes.Length - 1, Ipad));
        foreach (var pid in new[] { "129A", "129F", "12A2", "12A3", "12A4", "12A5", "12A6", "12A9", "12AB" })
            Check(ChargingIpad.Matches($@"USB\VID_05AC&PID_{pid}\MOCK"));
        Check(!ChargingIpad.Matches(@"USB\VID_05AC&PID_12A8\IPHONE"));
        Check(!ChargingIpad.Matches(@"USB\VID_05AC&PID_12AB&MI_00\FUNCTION"));
        Check(!ChargingIpad.Matches(@"USB\VID_DEAD&PID_12AB\OTHER"));
        Fails(() => ChargingIpad.Select([Device(), Device(Candidate)]), "one ready iPad");
        Fails(() => ChargingIpad.Select([Device() with { Ready = false }]), "one ready iPad");
    }

    internal static void Charging()
    {
        using var rig = new Rig();
        if (OperatingSystem.IsWindows())
            RunCharging(rig, new WindowsChargeCoordination(rig.Paths.ChargeLock, @"Local\LegacyIpadCharge-test-" + Guid.NewGuid()));
        else if (OperatingSystem.IsLinux())
            RunCharging(rig, new LinuxChargeCoordination(rig.Paths.UserData));
    }

    private static void RunCharging(Rig rig, IChargeCoordination coordination)
    {
        var commands = new ChargeCommands(rig, rig.Paths.UserData, coordination, "mock", TextWriter.Null, TextWriter.Null);
        commands.Check();
        rig.DevicesList = [Device(Candidate)];
        Fails(() => commands.Enable(2000), "No confirmed current limit");
        Check(rig.Requests.IsEmpty);
        Check(rig.OpenCount == 1);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FirstPort)));
        var oldProfile = Path.Combine(rig.Paths.UserData, "PortLimits", hash + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(oldProfile)!);
        File.WriteAllText(oldProfile, "2000");
        rig.DevicesList = [Device(Candidate) with { LocationPaths = [FirstPort.ToLowerInvariant(), FirstPort] }];
        commands.Enable(2000);
        commands.Restore(CancellationToken.None);
        Check(rig.Requests.SequenceEqual([1500, 0]));

        rig.Requests.Clear();
        rig.DevicesList = [Device(Candidate) with { LocationPaths = [SecondPort] }];
        commands.ConfirmPort(1000);
        Fails(() => commands.Enable(2000), "confirmed limit of 1000");
        Check(rig.Requests.IsEmpty);
        commands.Trial(1000, TimeSpan.Zero, CancellationToken.None);
        Check(rig.Requests.SequenceEqual([500, 0]));

        rig.Requests.Clear();
        rig.FailNextTransfer = true;
        Fails(() => commands.Enable(1000), "transfer interrupted");
        Check(rig.Requests.SequenceEqual([500, 0]));

        rig.Requests.Clear();
        using var ready = new ManualResetEventSlim();
        rig.TransferObserved = offset => { if (offset > 0) ready.Set(); };
        var trial = Task.Run(() => commands.Trial(1000, TimeSpan.FromSeconds(8), CancellationToken.None));
        Check(ready.Wait(TimeSpan.FromSeconds(4)));
        commands.Restore(CancellationToken.None);
        Check(trial.Wait(TimeSpan.FromSeconds(4)));
        Check(rig.Requests.SequenceEqual([500, 0, 0]));

        rig.Requests.Clear();
        using var canceled = new CancellationTokenSource();
        rig.TransferObserved = offset => { if (offset > 0) canceled.Cancel(); };
        try
        {
            commands.Trial(1000, TimeSpan.FromSeconds(8), canceled.Token);
            throw new InvalidOperationException("Expected a canceled trial.");
        }
        catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
        Check(rig.Requests.SequenceEqual([500, 0]));

        rig.Requests.Clear();
        rig.DevicesList = [Device(Candidate) with { LocationPaths = [] }];
        Fails(() => commands.ConfirmPort(2000), "single USB port path");
        Fails(() => commands.Trial(1000, TimeSpan.Zero, CancellationToken.None), "single USB port path");
        commands.Restore(CancellationToken.None);
        Check(rig.Requests.SequenceEqual([0]));
    }

    internal static void LinuxTopology()
    {
        using var rig = new Rig();
        var sysfs = Path.Combine(rig.Paths.Temp, "sysfs");
        var backend = new LinuxIpadSystem(sysfs);
        Check(backend.Read(0x05ac).Length == 0);
        Directory.CreateDirectory(sysfs);
        LinuxRoot(sysfs, 3, "0000:03:00.0", "2.00");
        LinuxRoot(sysfs, 4, "vhci_hcd.0", "2.00");
        var physical = LinuxDevice(sysfs, "3-2.1", "3", "7");
        var virtualIpad = LinuxDevice(sysfs, "4-2.1", "4", "2");
        LinuxDevice(sysfs, "3-2.1:1.0", "3", "7");
        var devices = backend.Read(0x05ac, 0x12a2);
        Check(devices.Length == 2 && devices[0].Ready && devices[1].Ready);
        Check(devices[0].PortPath == "linux:pci:0000:03:00.0:usb2.00:2.1");
        Check(devices[0].Ports.SequenceEqual([2, 1]) && devices[1].PortPath is null);

        Directory.Delete(virtualIpad, recursive: true);
        var first = ChargingIpad.Select(backend.Devices());
        File.WriteAllText(Path.Combine(physical, "devnum"), "8\n");
        var reconnected = ChargingIpad.Select(backend.Devices());
        Check(first.InstanceId != reconnected.InstanceId && backend.PortPath(first) == backend.PortPath(reconnected));
        Fails(() => backend.Open(first), "path changed");

        LinuxRoot(sysfs, 5, "0000:03:00.0", "2.00");
        Check(backend.PortPath(ChargingIpad.Select(backend.Devices())) is null);
    }

    private static void LinuxRoot(string sysfs, int bus, string controller, string version)
    {
        var root = Path.Combine(Path.GetDirectoryName(sysfs)!, controller, $"usb{bus}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "version"), version + "\n");
        Directory.CreateSymbolicLink(Path.Combine(sysfs, $"usb{bus}"), root);
    }

    private static string LinuxDevice(string sysfs, string name, string bus, string address)
    {
        var folder = Path.Combine(sysfs, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "idVendor"), "05ac\n");
        File.WriteAllText(Path.Combine(folder, "idProduct"), "12a2\n");
        File.WriteAllText(Path.Combine(folder, "busnum"), bus + "\n");
        File.WriteAllText(Path.Combine(folder, "devnum"), address + "\n");
        File.WriteAllText(Path.Combine(folder, "authorized"), "1\n");
        return folder;
    }

    internal static void Installation()
    {
        using var rig = new Rig();
        rig.DevicesList = [Device(Candidate)];
        rig.FailStaging = true;
        var command = new FilterInstallation(rig, rig.Paths, TextWriter.Null);
        Fails(() => command.Install(rig.Bin), "Staging libusb0 failed");
        var recorded = FilterReceipt.Read(rig.Paths.Journal);
        Check(recorded.InstanceId == Candidate && recorded.LibusbInf is null && File.Exists(recorded.RollbackFile));
        Fails(() => command.Remove(recorded.RollbackFile!, null), "--recover-staged-package");
        Fails(() => command.Remove(recorded.RollbackFile!, "oem43.inf"), "does not match");
        command.Remove(recorded.RollbackFile!, "oem42.inf");
        command.Remove(recorded.RollbackFile!, null);
        Check(rig.Operations.SequenceEqual(["/export-driver", "/add-driver", "/delete-driver"]));
        Check(!File.Exists(rig.Paths.Journal));

        rig.Operations.Clear();
        rig.FailStaging = false;
        command.Install(rig.Bin);
        var installed = FilterReceipt.Read(rig.Paths.Journal);
        Check(installed.LibusbInf == "oem42.inf" && File.Exists(rig.Paths.DriverFile));
        Check(rig.Operations.SequenceEqual(["/export-driver", "/add-driver", "install"]));
    }

    internal static void Rollback()
    {
        using var rig = new Rig();
        var command = new FilterInstallation(rig, rig.Paths, TextWriter.Null);
        var receipt = Path.Combine(rig.Paths.UserData, "old-receipt.clixml");
        const string installId = "58a6d59c53f94ac4aeb7b7df6cc63668";
        File.WriteAllText(receipt, $"""
            <Objs Version="1.1.0.1" xmlns="http://schemas.microsoft.com/powershell/2004/04">
              <Obj RefId="0"><TN RefId="0"><T>System.Management.Automation.PSCustomObject</T></TN>
              <MS><S N="InstallId">{installId}</S></MS></Obj>
            </Objs>
            """);
        var state = new FilterReceipt(installId, Ipad, "oem11.inf", rig.FilterTool, "oem42.inf", receipt);
        state.Save(rig.Paths.Journal);
        Check(FilterReceipt.Read(receipt).InstallId == installId);
        Check(FilterReceipt.Read(rig.Paths.Journal) == state);
        var unrelatedReceipt = Path.Combine(rig.Paths.UserData, "unrelated.clixml");
        new FilterReceipt(InstallId: Guid.NewGuid().ToString("N")).Save(unrelatedReceipt);
        Fails(() => command.Remove(unrelatedReceipt, null), "does not match the current installation");
        rig.StoredPackages = [new DriverPackage("oem42.inf", rig.StoreInf)];
        rig.UnmatchedFile = rig.StoreInf;
        Fails(() => command.Remove(receipt, null), "Driver Store package");
        rig.UnmatchedFile = null;
        rig.ClassFilter = true;
        Fails(() => command.Remove(receipt, null), "device class");
        rig.ClassFilter = false;
        rig.Bindings = [new FilterBinding(Candidate, FilterBindingKind.Upper)];
        Fails(() => command.Remove(receipt, null), "Another USB driver binding");
        Check(rig.Operations.Count == 0);

        rig.Bindings = [new FilterBinding(Ipad, FilterBindingKind.Upper), new FilterBinding(Ipad, FilterBindingKind.Stack)];
        rig.LibusbService = new DriverService(rig.Paths.DriverFile, 4);
        File.WriteAllText(rig.Paths.DriverFile, "test");
        command.Remove(receipt, null);
        Check(rig.Operations.SequenceEqual(["uninstall"]));
        Fails(() => command.Remove(receipt, null), "Reconnect the iPad");
        Check(File.Exists(rig.Paths.DriverFile) && File.Exists(rig.Paths.Journal));
        rig.Bindings = [];
        command.Remove(receipt, null);
        Check(rig.Operations.SequenceEqual(["uninstall", "stop", "/delete-driver", "delete"]));
        Check(!File.Exists(rig.Paths.DriverFile) && !File.Exists(rig.Paths.Journal));

        (state with { InstallId = null, LibusbInf = null, DriverPath = rig.Paths.DriverFile,
            DriverHash = LibusbFiles.DriverHash }).Save(receipt);
        rig.StoredPackages = [new DriverPackage("oem42.inf", rig.StoreInf)];
        Fails(() => command.Remove(receipt, null), "explicitly verified package");
        command.Remove(receipt, "oem42.inf");
        Check(rig.StoredPackages.Length == 0);
    }

    private static ChargeDevice Device(string id = Ipad) => new(id, true, "usbccgp", "oem11.inf", [FirstPort], ["libusb0"]);

    private static void Check(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("USB charging workflow assertion failed.");
    }

    private static void Fails(Action run, string message)
    {
        try { run(); }
        catch (IOException error) when (error.Message.Contains(message, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"Expected a failure containing '{message}'.");
    }

    private sealed class Rig : IChargeHardware, IFilterSystem, IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("legacy-ipad-mock-").FullName;

        internal Rig()
        {
            Paths = new ChargingPaths(Path.Combine(root, "system"), Path.Combine(root, "user"), root);
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.DriverFile)!);
            Directory.CreateDirectory(Paths.UserData);
            Directory.CreateDirectory(Path.Combine(Bin, "amd64"));
            File.WriteAllText(FilterTool, "test");
            File.WriteAllText(Path.Combine(Bin, "amd64", "libusb0.sys"), "test");
            File.WriteAllText(Path.Combine(Bin, "libusb0.inf"), "test");
            File.WriteAllText(StoreInf, "test");
        }

        internal ChargingPaths Paths { get; }
        internal string Bin => Path.Combine(root, "bin");
        internal string FilterTool => Path.Combine(Bin, "amd64", "install-filter.exe");
        internal string StoreInf => Path.Combine(root, "store.inf");
        internal ConcurrentQueue<int> Requests { get; } = new();
        internal List<string> Operations { get; } = [];
        internal ChargeDevice[] DevicesList { get; set; } = [Device()];
        internal DriverPackage[] StoredPackages { get; set; } = [];
        internal FilterBinding[] Bindings { get; set; } = [];
        internal DriverService? LibusbService { get; set; }
        internal string? UnmatchedFile { get; set; }
        internal bool ClassFilter { get; set; }
        internal bool FailStaging { get; set; }
        internal bool FailNextTransfer { get; set; }
        internal int OpenCount { get; private set; }
        internal Action<int>? TransferObserved { get; set; }

        public ChargeDevice[] Devices() => DevicesList;
        public string? PortPath(ChargeDevice device) => ChargingIpad.WindowsPortPath(device);
        public DriverPackage[] Packages() => StoredPackages;
        public DriverService? Service(string name) => name == "libusb0" ? LibusbService : null;
        public bool HasClassFilter() => ClassFilter;
        public FilterBinding[] FilterBindings() => Bindings;
        public bool MatchesHash(string path, string expected) => File.Exists(path) && path != UnmatchedFile;

        public IChargeConnection Open(ChargeDevice device)
        {
            OpenCount++;
            return new MockConnection(this);
        }

        private void Send(int offsetMa)
        {
            Requests.Enqueue(offsetMa);
            if (FailNextTransfer)
            {
                FailNextTransfer = false;
                throw new IOException("transfer interrupted");
            }
            TransferObserved?.Invoke(offsetMa);
        }

        public int Run(string executable, params string[] args)
        {
            Operations.Add(args[0]);
            switch (args[0])
            {
                case "/add-driver":
                    Check(File.Exists(Paths.Journal));
                    StoredPackages = [new DriverPackage("oem42.inf", StoreInf)];
                    return FailStaging ? 1 : 0;
                case "install":
                    var receipt = FilterReceipt.Read(Paths.Journal);
                    Check(receipt.LibusbInf == "oem42.inf");
                    Check(args[1] == "--device-id=" + receipt.InstanceId);
                    break;
                case "uninstall":
                    Check(args[1] == "--device-id=" + FilterReceipt.Read(Paths.Journal).InstanceId);
                    Bindings = Bindings.Where(b => b.Kind != FilterBindingKind.Upper).ToArray();
                    break;
                case "/delete-driver":
                    Check(args[1] == "oem42.inf");
                    StoredPackages = [];
                    break;
                case "stop":
                    LibusbService = new DriverService(Paths.DriverFile, 1);
                    break;
                case "delete":
                    LibusbService = null;
                    break;
            }
            return 0;
        }

        void IDisposable.Dispose() => Directory.Delete(root, recursive: true);

        private sealed class MockConnection(Rig rig) : IChargeConnection
        {
            public void Send(int offsetMa) => rig.Send(offsetMa);
            public void Dispose() { }
        }
    }
}
