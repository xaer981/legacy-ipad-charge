using System.Buffers.Binary;
using System.Runtime.InteropServices;
using LegacyIpadCharge;
using Microsoft.Win32.SafeHandles;

var request = new WinUsbSetupPacket(0x80, 0, 0, 0, 2);
byte[] expected = [0x80, 0, 0, 0, 0, 0, 2, 0];
if (!MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref request, 1)).SequenceEqual(expected))
    throw new InvalidOperationException("Unexpected USB GET_STATUS setup packet layout.");

if (args is ["--self-test"])
{
    Console.WriteLine("PASS Standard device GET_STATUS setup packet (8000000000000200).");
    return 0;
}
if (args is not ["--run-once"] || !OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Use --self-test or --run-once on Windows. The live probe opens the Apple WinUSB interface.");
    return 2;
}

try
{
    return await Task.Run(() => Probe(request)).WaitAsync(TimeSpan.FromSeconds(10));
}
catch (TimeoutException)
{
    // The probe owns all handles in its own process, so Windows closes them on exit.
    Console.Error.WriteLine("WinUSB probe exceeded 10 seconds. No repeat attempted.");
    return 1;
}
catch (IOException error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static int Probe(WinUsbSetupPacket request)
{
    var target = new UsbIdentity(0x05AC, 0x12A2, 1);
    var ids = ConfigurationManager.UsbInstanceIds().Where(id => UsbIdentity.Parse(id) == target).ToArray();
    var devices = DeviceDiscovery.Read(target.Vendor, target.Product, queryHub: false);
    if (ids.Length != 1 || devices.Length != 1 ||
        !devices[0].Functions.Any(node => node is { InterfaceNumber: 1, Started: true } &&
            string.Equals(node.Service, "WINUSB", StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine("Expected exactly one iPad 05AC:12A2 with started WinUSB function MI_01.");
        return 3;
    }

    var appleInterface = new Guid("664be590-54bd-4964-8a8c-6cd1314f6dc2");
    var paths = ConfigurationManager.InterfacePaths(appleInterface, ids[0]);
    if (paths.Length != 1)
    {
        Console.Error.WriteLine("Expected exactly one active Apple WinUSB device interface.");
        return 3;
    }

    Console.WriteLine("Target: 05AC:12A2/MI_01, Apple iTunes WinUSB device interface.");
    using var file = Native.CreateFileW(paths[0], 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
    if (file.IsInvalid)
        return Fail("CreateFileW");
    Console.WriteLine("CreateFileW succeeded.");

    if (!Native.WinUsb_Initialize(file, out var winUsb))
        return Fail("WinUsb_Initialize");
    try
    {
        Console.WriteLine("WinUsb_Initialize succeeded.");
        byte[] status = new byte[request.Length];
        if (!Native.WinUsb_ControlTransfer(winUsb, request, status, (uint)status.Length,
                out var transferred, IntPtr.Zero))
            return Fail("WinUsb_ControlTransfer(GET_STATUS)");
        if (transferred != status.Length)
        {
            Console.Error.WriteLine($"GET_STATUS returned {transferred} bytes instead of 2.");
            return 1;
        }

        Console.WriteLine($"GET_STATUS succeeded: 2 bytes, status=0x{BinaryPrimitives.ReadUInt16LittleEndian(status):X4}.");
        return 0;
    }
    finally
    {
        Native.WinUsb_Free(winUsb);
    }
}

static int Fail(string operation)
{
    var error = Marshal.GetLastPInvokeError();
    Console.Error.WriteLine($"{operation} failed (Win32 {error}).");
    return 1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly record struct WinUsbSetupPacket(byte RequestType, byte Request, ushort Value,
    ushort Index, ushort Length);

internal static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("winusb.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_Initialize(SafeFileHandle file, out IntPtr handle);

    [DllImport("winusb.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_ControlTransfer(IntPtr handle, WinUsbSetupPacket setupPacket,
        [Out] byte[] buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_Free(IntPtr handle);
}
