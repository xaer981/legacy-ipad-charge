using System.Globalization;
using System.Text.Json;

namespace LegacyIpadCharge;

internal static class Diagnostics
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static void WriteJson(TextWriter writer, object result) =>
        writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

    internal const string BatteryCsvHeader =
        "observed_at_utc,instant_amperage_ma,voltage_mv,adapter_voltage_mv,adapter_amperage_ma,adapter_watts," +
        "adapter_description,external_connected,is_charging,cycle_count,apple_raw_max_capacity_mah," +
        "design_capacity_mah,temperature_raw";

    internal static void WriteBatteryCsv(TextWriter writer, BatterySample sample) =>
        WriteRow(writer, sample.ObservedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
            sample.InstantAmperageMa, sample.VoltageMv, sample.AdapterDetails?.VoltageMv,
            sample.AdapterDetails?.AmperageMa, sample.AdapterDetails?.Watts, sample.AdapterDetails?.Description,
            sample.ExternalConnected, sample.IsCharging, sample.CycleCount, sample.AppleRawMaxCapacityMah,
            sample.DesignCapacityMah, sample.TemperatureRaw);

    internal static void WriteDevicesCsv(TextWriter writer, UsbDevice[] devices)
    {
        writer.WriteLine("vid,pid,model_from_usb_id,location_path,port_chain,parent_hub_usb_id,speed," +
            "active_configuration,connection_status,node_kind,interface_number,service,upper_filters," +
            "lower_filters,driver_stack,interface_class_guids,winrt_usb_restricted,started,problem_code");
        foreach (var device in devices)
        foreach (var node in device.Functions.Prepend(device.Device))
        {
            WriteRow(writer, device.Vid, device.Pid, device.ModelFromUsbId, device.LocationPath,
                string.Join('-', device.PortChain), device.ParentHubUsbId, device.Connection?.Speed,
                device.Connection?.ActiveConfiguration, device.ConnectionStatus, node.Kind, node.InterfaceNumber,
                node.Service, string.Join(' ', node.UpperFilters), string.Join(' ', node.LowerFilters),
                string.Join(' ', node.DriverStack), string.Join(' ', node.InterfaceClassGuids),
                node.WinRtUsbRestricted, node.Started, node.ProblemCode);
        }
    }

    private static void WriteRow(TextWriter writer, params object?[] cells) =>
        writer.WriteLine(string.Join(',', cells.Select(EncodeCell)));

    private static string EncodeCell(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (value is string && text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
            text = "'" + text;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? '"' + text.Replace("\"", "\"\"") + '"' : text;
    }
}
