using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LegacyIpadCharge;

internal sealed class LibusbConnection : IChargeConnection
{
    private const uint VendorWrite = 0x222030;
    private const uint GetObjectName = 0x2223FC;
    private const int RequestSize = 24;
    private readonly SafeFileHandle handle;

    private LibusbConnection(SafeFileHandle handle) => this.handle = handle;

    internal static byte[] Request(int offsetMa)
    {
        // libusb0 uses vendor type 2. The Apple active current offset is in wIndex.
        var request = new byte[RequestSize];
        BinaryPrimitives.WriteUInt32LittleEndian(request, 3000);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(12), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(20), checked((uint)offsetMa));
        return request;
    }

    internal static LibusbConnection Open(string instanceId)
    {
        var names = new char[65536];
        var length = QueryDosDeviceW(null, names, (uint)names.Length);
        if (length == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Listing libusb0 links failed.");
        var links = new string(names, 0, (int)length).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(name => Regex.IsMatch(name, @"\Alibusb0-[0-9]{4}\z")).ToArray();
        if (links is not [var link])
            throw new IOException("Expected one libusb0 device link. Reconnect the iPad after installing the filter.");

        var handle = CreateFileW(@"\\.\" + link, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Opening the iPad filter failed.");

            // GetObjectName reads the filter's cached PnP path instead of sending a USB request.
            var path = new byte[4096];
            if (!DeviceIoControl(handle, GetObjectName, new byte[RequestSize], RequestSize,
                    path, (uint)path.Length, out var count, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Reading filter identity failed.");
            if (!MatchesInstance(path, count, instanceId))
                throw new IOException("The libusb0 handle does not belong to the selected iPad.");
            return new LibusbConnection(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static bool MatchesInstance(byte[] buffer, uint count, string instanceId) =>
        count >= 2 && count < buffer.Length && buffer[count - 1] == 0 &&
        Encoding.ASCII.GetString(buffer, 0, (int)count - 1)
            .EndsWith(@"\Enum\" + instanceId + @"\Device Parameters", StringComparison.OrdinalIgnoreCase);

    public void Send(int offsetMa)
    {
        if (!DeviceIoControl(handle, VendorWrite, Request(offsetMa), RequestSize,
                null, 0, out var count, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Apple vendor request failed.");
        if (count != 0)
            throw new IOException("Unexpected data in the vendor response.");
    }

    public void Dispose() => handle.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string? name, [Out] char[] target, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint sharing,
        IntPtr attributes, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, uint inputLength,
        [Out] byte[]? output, uint outputLength, out uint bytesReturned, IntPtr overlapped);
}
