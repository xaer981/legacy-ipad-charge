using System.Buffers.Binary;

namespace LegacyIpadCharge;

internal readonly record struct UsbSetupPacket(byte RequestType, byte Request, ushort Value, ushort Index,
    ushort Length)
{
    internal byte[] ToBytes()
    {
        byte[] bytes = [RequestType, Request, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), Value);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), Index);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), Length);
        return bytes;
    }
}

internal static class AppleChargingProtocol
{
    // Reference from FD-/usb_charge. Field semantics and response on iPad 2 remain unverified.
    internal static readonly UsbSetupPacket LinuxIpadFixture = new(0x40, 0x40, 500, 1600, 0);

    internal static bool IsKnownFixture(UsbSetupPacket packet) => packet == LinuxIpadFixture;
}

internal enum ChargingState { Unsupported, ReadOnly }

internal sealed record UsbTransportCapabilities(string State, bool? WinUsbServiceSeen, string Reason)
{
    internal static UsbTransportCapabilities Assess(UsbDevice[] devices)
    {
        if (devices.Length == 0)
            return new(nameof(ChargingState.Unsupported), null, "No matching present device.");
        var nodes = devices.SelectMany(device => device.Functions.Prepend(device.Device));
        var winUsbSeen = nodes.Any(node => string.Equals(node.Service, "WinUSB", StringComparison.OrdinalIgnoreCase));
        return new(nameof(ChargingState.ReadOnly), winUsbSeen, winUsbSeen
            ? "WinUSB service is visible in PnP. WinUSB endpoint 0 access remains unverified."
            : "No WinUSB service is visible in PnP. Use charge check to verify the libusb0 filter.");
    }
}

internal interface IUsbChargingTransport
{
    bool Submit(UsbSetupPacket packet);
}

internal static class ChargingController
{
    // This diagnostic fixture has no production transport.
    internal static ChargingState Simulate(UsbSetupPacket packet, IUsbChargingTransport? transport)
    {
        if (!AppleChargingProtocol.IsKnownFixture(packet))
            throw new ArgumentException("Unknown reference packet.", nameof(packet));
        return transport?.Submit(packet) is true ? ChargingState.ReadOnly : ChargingState.Unsupported;
    }
}
