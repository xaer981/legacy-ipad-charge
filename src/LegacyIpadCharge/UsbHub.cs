using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LegacyIpadCharge;

internal sealed record HubConnection(string Speed, byte? ActiveConfiguration);

internal static class UsbHub
{
    // CTL_CODE(FILE_DEVICE_USB, function, METHOD_BUFFERED, FILE_ANY_ACCESS).
    private const uint GetConnectionInfo = (0x22u << 16) | (274u << 2);
    private const uint GetConnectionInfoV2 = (0x22u << 16) | (279u << 2);
    private const int ConnectionInfoSize = 35;
    private const uint Usb300Protocol = 4;

    internal static (HubConnection? Connection, string Status) Read(string hubPath, int port, UsbIdentity expected)
    {
        // Share access to the hub and request no generic read/write rights.
        using var handle = CreateFileW(hubPath, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return (null, $"hub-open-error-{Marshal.GetLastPInvokeError()}");

        byte[] info = new byte[ConnectionInfoSize];
        BinaryPrimitives.WriteInt32LittleEndian(info, port);
        if (!DeviceIoControl(handle, GetConnectionInfo, info, info.Length, out var returned))
            return (null, $"hub-query-error-{Marshal.GetLastPInvokeError()}");
        if (returned < ConnectionInfoSize)
            return (null, "hub-response-too-short");

        byte[] v2 = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(v2, port);
        BinaryPrimitives.WriteInt32LittleEndian(v2.AsSpan(4), v2.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(8), Usb300Protocol);
        uint? speedFlags = DeviceIoControl(handle, GetConnectionInfoV2, v2, v2.Length, out returned) &&
            returned >= v2.Length ? BinaryPrimitives.ReadUInt32LittleEndian(v2.AsSpan(12)) : null;
        var connection = Decode(info, expected, speedFlags);
        return (connection, connection is null ? "port-disconnected-or-device-changed" : "read");
    }

    internal static HubConnection? Decode(ReadOnlySpan<byte> info, UsbIdentity expected, uint? speedFlags)
    {
        // Windows SDK usbioctl.h packs the connection header and embedded device descriptor to one byte.
        if (info.Length < ConnectionInfoSize || BinaryPrimitives.ReadUInt32LittleEndian(info[31..]) != 1 ||
            info[4] != 18 || info[5] != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(info[12..]) != expected.Vendor ||
            BinaryPrimitives.ReadUInt16LittleEndian(info[14..]) != expected.Product)
            return null;

        var speed = info[23] switch
        {
            0 => "low",
            1 => "full",
            2 or 3 when (speedFlags & 4) == 4 => "super-plus-or-higher",
            2 or 3 when (speedFlags & 1) == 1 => "super",
            2 when speedFlags.HasValue => "high",
            2 => "high-or-super",
            3 => "super",
            _ => "unknown"
        };
        return new HubConnection(speed, info[22] == 0 ? null : info[22]);
    }

    private static bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] buffer, int size, out int returned) =>
        QueryPort(handle, code, buffer, size, buffer, size, out returned, IntPtr.Zero);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryPort(SafeFileHandle handle, uint code, [In] byte[] input,
        int inputSize, [Out] byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
