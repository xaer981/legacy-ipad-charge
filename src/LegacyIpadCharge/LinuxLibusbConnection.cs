using System.Runtime.InteropServices;

namespace LegacyIpadCharge;

internal sealed class LinuxLibusbConnection(nint context, nint handle) : IChargeConnection
{
    private const string Library = "libusb-1.0.so.0";
    private nint connection = handle;

    internal static LinuxLibusbConnection Open(LinuxUsbDevice expected)
    {
        nint context;
        try { Check(libusb_init(out context), "Initializing libusb"); }
        catch (DllNotFoundException error)
        {
            throw new IOException("Install the system libusb-1.0 runtime (libusb-1.0.so.0).", error);
        }

        try
        {
            var count = libusb_get_device_list(context, out var list);
            if (count < 0)
                throw Failure("Listing USB devices", (int)count);

            try
            {
                for (var index = 0; index < count; index++)
                {
                    var device = Marshal.ReadIntPtr(list, index * IntPtr.Size);
                    if (libusb_get_bus_number(device) != expected.Bus ||
                        libusb_get_device_address(device) != expected.Address)
                        continue;

                    var ports = new byte[expected.Ports.Length];
                    Check(libusb_get_device_descriptor(device, out var descriptor), "Reading the USB device identity");
                    var length = libusb_get_port_numbers(device, ports, ports.Length);
                    if (descriptor.Vendor != expected.Identity.Vendor || descriptor.Product != expected.Identity.Product ||
                        length != ports.Length || !expected.Ports.SequenceEqual(ports.Select(port => (int)port)))
                        throw new IOException("The selected iPad or its USB path changed. Reconnect and retry.");

                    Check(libusb_open(device, out var handle), "Opening the iPad");
                    return new LinuxLibusbConnection(context, handle);
                }
            }
            finally { libusb_free_device_list(list, 1); }

            throw new IOException("The iPad disconnected before libusb could open it. Reconnect and retry.");
        }
        catch
        {
            libusb_exit(context);
            throw;
        }
    }

    public void Send(int offsetMa)
    {
        ObjectDisposedException.ThrowIf(connection == 0, this);
        var packet = AppleChargingProtocol.ActiveCurrent(offsetMa);
        var count = libusb_control_transfer(connection, packet.RequestType, packet.Request, packet.Value, packet.Index,
            nint.Zero, packet.Length, 3000);
        Check(count, "Sending the Apple charging request");
        if (count != 0)
            throw new IOException("Unexpected data in the vendor response.");
    }

    public void Dispose()
    {
        if (connection == 0)
            return;
        libusb_close(connection);
        connection = 0;
        libusb_exit(context);
    }

    private static void Check(int result, string operation)
    {
        if (result < 0)
            throw Failure(operation, result);
    }

    private static IOException Failure(string operation, int result) => result == -3
        ? new IOException($"{operation} failed: permission denied. See the Linux guide for USB device permissions.")
        : new IOException($"{operation} failed: {Marshal.PtrToStringAnsi(libusb_error_name(result))}.");

    [StructLayout(LayoutKind.Explicit, Size = 18)]
    private struct DeviceDescriptor
    {
        [FieldOffset(8)] public ushort Vendor;
        [FieldOffset(10)] public ushort Product;
    }

    [DllImport(Library)] private static extern int libusb_init(out nint context);
    [DllImport(Library)] private static extern void libusb_exit(nint context);
    [DllImport(Library)] private static extern nint libusb_get_device_list(nint context, out nint list);
    [DllImport(Library)] private static extern void libusb_free_device_list(nint list, int unrefDevices);
    [DllImport(Library)] private static extern byte libusb_get_bus_number(nint device);
    [DllImport(Library)] private static extern byte libusb_get_device_address(nint device);
    [DllImport(Library)] private static extern int libusb_get_port_numbers(nint device, [Out] byte[] ports, int length);
    [DllImport(Library)] private static extern int libusb_get_device_descriptor(nint device, out DeviceDescriptor descriptor);
    [DllImport(Library)] private static extern int libusb_open(nint device, out nint handle);
    [DllImport(Library)] private static extern void libusb_close(nint handle);
    [DllImport(Library)] private static extern nint libusb_error_name(int error);
    [DllImport(Library)] private static extern int libusb_control_transfer(nint handle, byte requestType, byte request,
        ushort value, ushort index, nint data, ushort length, uint timeout);
}
