using System.Buffers.Binary;
using LegacyIpadCharge;

var tests = new (string Name, Action Run)[]
{
    ("Windows physical and USB interface IDs", () =>
    {
        Equal(new UsbIdentity(0x05AC, 0x12A2, null), UsbIdentity.Parse(@"USB\VID_05AC&PID_12A2\synthetic"));
        Equal(new UsbIdentity(0x05AC, 0x12A2, 1), UsbIdentity.Parse(@"usb\vid_05ac&pid_12a2&mi_01\synthetic"));
        Equal(new UsbIdentity(0x05E3, 0x0608, null), UsbIdentity.Parse(@"USB\VID_05E3&PID_0608\synthetic"));
        Equal(null, UsbIdentity.Parse(@"USB\VID_05AC&PID_12A2&MI_7\invalid"));
        Equal(null, UsbIdentity.Parse(@"PCI\VID_05AC&PID_12A2\invalid"));
    }),
    ("Hub connection header, stale device and SuperSpeed", () =>
    {
        var usb = new UsbIdentity(0x05AC, 0x12A2, null);
        var bytes = new byte[35];
        bytes[4] = 18;
        bytes[5] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), usb.Vendor);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), usb.Product);
        bytes[22] = 3;
        bytes[23] = 2;
        bytes[31] = 1;
        Equal(new HubConnection("high", 3), UsbHub.Decode(bytes, usb, 0));
        Equal(new HubConnection("super-plus-or-higher", 3), UsbHub.Decode(bytes, usb, 5));
        Equal(new HubConnection("high-or-super", 3), UsbHub.Decode(bytes, usb, null));
        Equal(null, UsbHub.Decode(bytes.AsSpan(0, 34), usb, 0));
        Equal(null, UsbHub.Decode(bytes, usb with { Product = 0x12A3 }, 0));
        bytes[31] = 0;
        Equal(null, UsbHub.Decode(bytes, usb, 0));
    }),
    ("Signed and wrapped battery currents", () =>
    {
        Equal(-353L, BatteryTelemetry.ParseCurrent("18446744073709551263"));
        Equal(-494L, BatteryTelemetry.ParseCurrent("18446744073709551122"));
        Equal(-494L, BatteryTelemetry.ParseCurrent("-494"));
        Equal(29L, BatteryTelemetry.ParseCurrent("29"));
        Equal(long.MinValue, BatteryTelemetry.ParseCurrent("9223372036854775808"));
        Equal(-1L, BatteryTelemetry.ParseCurrent("18446744073709551615"));
        Equal(null, BatteryTelemetry.ParseCurrent("18446744073709551616"));
    }),
    ("ioreg fixture, battery/adapter units and reports", () =>
    {
        var sample = BatteryTelemetry.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "charger.txt")));
        Equal(-494L, sample.InstantAmperageMa);
        Equal(3971, sample.VoltageMv);
        Equal(true, sample.IsCharging);
        Equal(true, sample.ExternalConnected);
        Equal(545, sample.CycleCount);
        Equal(5325, sample.AppleRawMaxCapacityMah);
        Equal(6583, sample.DesignCapacityMah);
        Equal(2920, sample.TemperatureRaw);
        Equal(new AdapterDetails(5000, 500, 2, "usb host"), sample.AdapterDetails);
        using var output = new StringWriter();
        Diagnostics.WriteJson(output, sample);
        Diagnostics.WriteBatteryCsv(output, sample);
        var report = output.ToString();
        Equal(false, report.Contains("SYNTHETIC-PRIVATE", StringComparison.Ordinal));
        Equal(false, report.Contains("battery-data", StringComparison.Ordinal));
        Equal(true, report.Contains(",-494,3971,5000,500,2,usb host,", StringComparison.Ordinal));
    }),
    ("Multiline adapters, optional data and unknown descriptions", () =>
    {
        var sample = BatteryTelemetry.Parse("""
              "ExternalConnected" = No
              "AdapterDetails" = {
                  "Description" = "SYNTHETIC-PRIVATE"
                  "Voltage" = 5000
                  "nested" = {"Watts"=12}
                  "Amperage" = invalid
              }
            """);
        Equal(null, sample.InstantAmperageMa);
        Equal(null, sample.CycleCount);
        Equal(false, sample.ExternalConnected);
        Equal(new AdapterDetails(5000, null, null, null), sample.AdapterDetails);
        Throws<FormatException>(() => BatteryTelemetry.Parse("invalid ioreg output"));
        Throws<FormatException>(() => BatteryTelemetry.Parse("\"Voltage\"=5000\n\"Voltage\"=4000"));
    }),
    ("Unusable readings and distinct ioreg nodes", () =>
    {
        Throws<FormatException>(() => BatteryTelemetry.Parse("\"InstantAmperage\" = invalid"));
        Throws<FormatException>(() => BatteryTelemetry.Parse("\"AdapterDetails\" = {\"Amperage\"=invalid}"));
        Throws<FormatException>(() => BatteryTelemetry.Parse("""
            +-o First <class AppleARMPMUCharger, active>
              | "InstantAmperage" = -494
            +-o Second <class AppleARMPMUCharger, active>
              | "IsCharging" = Yes
            """));
        var sample = BatteryTelemetry.Parse("""
            +-o Parent <class IOService, active>
              | "Voltage" = 123
              +-o Charger <class AppleARMPMUCharger, active>
                | "InstantAmperage" = -494
                +-o Child <class IOService, active>
                  | "IsCharging" = Yes
            """);
        Equal(-494L, sample.InstantAmperageMa);
        Equal(null, sample.VoltageMv);
        Equal(null, sample.IsCharging);
    }),
    ("Reference packet encoding and guarded mock transport", () =>
    {
        var packet = AppleChargingProtocol.LinuxIpadFixture;
        Equal("4040F40140060000", Convert.ToHexString(packet.ToBytes()));
        Equal(ChargingState.Unsupported, ChargingController.Simulate(packet, null));
        var mock = new MockTransport();
        Throws<ArgumentException>(() => ChargingController.Simulate(packet with { Length = 1 }, mock));
        Equal(0, mock.Calls);
        Equal(ChargingState.ReadOnly, ChargingController.Simulate(packet, mock));
        Equal(1, mock.Calls);
        Equal(packet, mock.LastPacket);
    }),
    ("Dry-run capability remains read-only with an existing WinUSB service", () =>
    {
        Equal("Unsupported", UsbTransportCapabilities.Assess([]).State);
        var node = new DeviceNode("physical", null, "usbccgp", [], [], [], [], null, true, null);
        var device = new UsbDevice("05AC", "12A2", null, null, [], null, null, "not-queried", node, []);
        Equal(false, UsbTransportCapabilities.Assess([device]).WinUsbServiceSeen);
        var withWinUsb = device with { Functions = [node with { Kind = "function", Service = "WINUSB" }] };
        var capability = UsbTransportCapabilities.Assess([withWinUsb]);
        Equal(true, capability.WinUsbServiceSeen);
        Equal("ReadOnly", capability.State);
    }),
    ("SSH monitoring continues after a failed sample", () =>
    {
        using var stop = new CancellationTokenSource();
        using var messages = new StringWriter();
        var good = BatteryTelemetry.Parse("\"InstantAmperage\" = -494");
        var calls = 0;
        var readings = new List<BatterySample>();

        Throws<OperationCanceledException>(() => BatteryTelemetry.Monitor(
            TimeSpan.Zero, TimeSpan.FromSeconds(5),
            _ => ++calls == 1 ? Task.FromException<BatterySample>(new IOException("sample lost")) : Task.FromResult(good),
            sample =>
            {
                readings.Add(sample);
                stop.Cancel();
            }, messages, stop.Token).GetAwaiter().GetResult());

        Equal(2, calls);
        Equal(good, readings.Single());
        Equal(true, messages.ToString().Contains("sample lost", StringComparison.Ordinal));
    }),
    ("SSH destination validation", () =>
    {
        Equal(true, BatteryTelemetry.ValidSshHost("mobile@ipad.local"));
        Equal(true, BatteryTelemetry.ValidSshHost("root@192.0.2.4"));
        Equal(true, BatteryTelemetry.ValidSshHost("root@2001:db8::1"));
        Equal(false, BatteryTelemetry.ValidSshHost("-oProxyCommand=anything"));
        Equal(false, BatteryTelemetry.ValidSshHost("root@ipad.local command"));
        Equal(false, BatteryTelemetry.ValidSshHost("root@ipad@local"));
    })
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
    }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

internal sealed class MockTransport : IUsbChargingTransport
{
    internal int Calls { get; private set; }
    internal UsbSetupPacket? LastPacket { get; private set; }

    public bool Submit(UsbSetupPacket packet)
    {
        Calls++;
        LastPacket = packet;
        return true;
    }
}
