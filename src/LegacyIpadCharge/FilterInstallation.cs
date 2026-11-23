using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LegacyIpadCharge;

internal sealed record DriverPackage(string PublishedName, string InfPath);
internal sealed record DriverService(string? Path, uint State)
{
    internal bool Stopped => State == 1;
    internal bool Running => State == 4;
}
internal enum FilterBindingKind { Upper, Lower, Service, Stack }
internal sealed record FilterBinding(string InstanceId, FilterBindingKind Kind);

internal interface IFilterSystem
{
    ChargeDevice[] Devices();
    DriverPackage[] Packages();
    DriverService? Service(string name);
    bool HasClassFilter();
    FilterBinding[] FilterBindings();
    bool MatchesHash(string path, string expected);
    int Run(string executable, params string[] arguments);
}

internal sealed record FilterReceipt(string? InstallId = null, string? InstanceId = null, string? AppleInf = null,
    string? FilterTool = null, string? LibusbInf = null, string? RollbackFile = null,
    string? DriverPath = null, string? DriverHash = null)
{
    private static readonly XNamespace CliXml = "http://schemas.microsoft.com/powershell/2004/04";
    private static readonly Regex EscapedCharacter = new(@"_x([0-9a-fA-F]{4})_", RegexOptions.CultureInvariant);

    internal static FilterReceipt Read(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 65536
        });
        var members = XDocument.Load(reader).Root?.Element(CliXml + "Obj")?.Element(CliXml + "MS")
            ?? throw new IOException("Invalid libusb0 rollback file or installation journal.");
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members.Elements())
        {
            var name = (string?)member.Attribute("N");
            if (name is null || member.HasElements ||
                member.Name != CliXml + "S" && member.Name != CliXml + "Nil" ||
                !values.TryAdd(name, member.Name == CliXml + "Nil" ? null : Decode(member.Value)))
                throw new IOException("Invalid or duplicate field in the libusb0 rollback file.");
        }
        return new FilterReceipt(values.GetValueOrDefault("InstallId"), values.GetValueOrDefault("InstanceId"),
            values.GetValueOrDefault("AppleInf"), values.GetValueOrDefault("FilterTool"),
            values.GetValueOrDefault("LibusbInf"), values.GetValueOrDefault("RollbackFile"),
            values.GetValueOrDefault("DriverPath"), values.GetValueOrDefault("DriverHash"));
    }

    internal void Save(string path)
    {
        var members = new XElement(CliXml + "MS", Field(nameof(InstallId), InstallId),
            Field(nameof(InstanceId), InstanceId), Field(nameof(AppleInf), AppleInf),
            Field(nameof(FilterTool), FilterTool), Field(nameof(LibusbInf), LibusbInf),
            Field(nameof(RollbackFile), RollbackFile), Field(nameof(DriverPath), DriverPath),
            Field(nameof(DriverHash), DriverHash));
        // Keep the journal and receipt readable by older PowerShell releases.
        var xml = new XElement(CliXml + "Objs", new XAttribute("Version", "1.1.0.1"),
            new XElement(CliXml + "Obj", new XAttribute("RefId", "0"), members));
        LibusbFiles.WriteAtomic(path, xml.ToString());
    }

    private static XElement Field(string name, string? value) => value is null
        ? new XElement(CliXml + "Nil", new XAttribute("N", name))
        : new XElement(CliXml + "S", new XAttribute("N", name),
            EscapedCharacter.Replace(value, match => "_x005F_" + match.Value[1..]));

    private static string Decode(string value) => EscapedCharacter.Replace(value, match =>
        ((char)ushort.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
}

internal sealed class FilterInstallation(IFilterSystem system, ChargingPaths paths, TextWriter output)
{
    private readonly string installLock = Path.ChangeExtension(paths.Journal, "lock");
    private readonly string pnputil = Path.Combine(paths.SystemDirectory, "pnputil.exe");
    private readonly string serviceControl = Path.Combine(paths.SystemDirectory, "sc.exe");
    private static readonly Regex PublishedInf = new(@"\Aoem[0-9]+\.inf\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal void Install(string bin)
    {
        var directory = Path.GetFullPath(bin);
        var tool = Path.Combine(directory, "amd64", "install-filter.exe");
        var driver = Path.Combine(directory, "amd64", "libusb0.sys");
        var inf = Path.Combine(directory, "libusb0.inf");
        if (!system.MatchesHash(tool, LibusbFiles.ToolHash) || !system.MatchesHash(driver, LibusbFiles.DriverHash) ||
            !system.MatchesHash(inf, LibusbFiles.InfHash))
            throw new IOException("Use the original libusb-win32 1.4.0.2 package from the installation guide.");

        using var operation = LibusbFiles.Lock(installLock);
        if (File.Exists(paths.Journal))
            throw new IOException($"An installation is already recorded. Use its rollback file: {FilterReceipt.Read(paths.Journal).RollbackFile}");
        if (system.Service("libusb0") is not null || system.Service("libusbd") is not null ||
            File.Exists(paths.DriverFile) || system.Packages().Length != 0 || system.HasClassFilter() ||
            system.FilterBindings().Length != 0)
            throw new IOException("An existing libusb0 installation or binding needs to be rolled back first.");

        var device = ChargingIpad.Select(system.Devices());
        if (!Same(device.Service, "usbccgp") || !IsPublishedInf(device.DriverInf))
            throw new IOException("Expected the original Apple composite USB stack.");

        var id = Guid.NewGuid().ToString("N");
        var backup = Path.Combine(paths.UserData, "Backups", id);
        Directory.CreateDirectory(backup);
        if (system.Run(pnputil, "/export-driver", device.DriverInf!, backup) != 0)
            throw new IOException("Apple driver package backup failed.");

        var receiptPath = Path.Combine(backup, "libusb-rollback.clixml");
        new FilterReceipt(InstallId: id).Save(receiptPath);
        var state = new FilterReceipt(id, device.InstanceId, device.DriverInf, tool, RollbackFile: receiptPath);
        state.Save(paths.Journal);
        output.WriteLine($"Rollback file: {receiptPath}");

        if (system.Run(pnputil, "/add-driver", inf) != 0)
            throw new IOException("Staging libusb0 failed. Use the saved rollback file.");
        if (system.Packages() is not [var package] || !MatchesPackage(package))
            throw new IOException("Could not identify the staged libusb0 package. Use the saved rollback file.");
        (state with { LibusbInf = package.PublishedName }).Save(paths.Journal);

        File.Copy(driver, paths.DriverFile, overwrite: false);
        RunFilter(state, "install", "Filter installation failed. Use the saved rollback file.");
        output.WriteLine("Filter installation requested. Reconnect the iPad and run charge check before charging.");
    }

    internal void Remove(string receiptPath, string? recoverPackage)
    {
        if (recoverPackage is not null && !IsPublishedInf(recoverPackage))
            throw new ArgumentException("Use an oemNN.inf name for --recover-staged-package.");
        using var operation = LibusbFiles.Lock(installLock);
        var hasJournal = File.Exists(paths.Journal);
        var state = ReadRollback(receiptPath, hasJournal, recoverPackage);
        if (state is null)
        {
            output.WriteLine("No active installation matches this rollback file.");
            return;
        }

        using var charging = LibusbFiles.Lock(paths.ChargeLock);
        var package = ResolvePackage(state.LibusbInf ?? recoverPackage);
        var hasDriverFile = File.Exists(paths.DriverFile);
        if (hasDriverFile && !system.MatchesHash(paths.DriverFile, LibusbFiles.DriverHash))
            throw new IOException("The installed libusb0.sys does not match the tested release.");
        var service = system.Service("libusb0");
        if (system.Service("libusbd") is not null || service is not null && !Same(service.Path, paths.DriverFile))
            throw new IOException("The existing libusb service is outside this rollback.");
        if (system.HasClassFilter())
            throw new IOException("A device class still uses libusb0.");
        var bindings = system.FilterBindings();
        if (bindings.Any(binding => !Same(binding.InstanceId, state.InstanceId) ||
                binding.Kind is not (FilterBindingKind.Upper or FilterBindingKind.Stack)))
            throw new IOException("Another USB driver binding still uses libusb0.");
        if (state.LibusbInf is null && recoverPackage is null && (hasDriverFile || service is not null || bindings.Length > 0))
            throw new IOException("Driver components exist without a recorded package. Verify the installation before continuing.");
        if (hasJournal && state.LibusbInf is null && package is not null)
            (state with { LibusbInf = package.PublishedName }).Save(paths.Journal);

        if (bindings.Any(binding => binding.Kind == FilterBindingKind.Upper))
        {
            RunFilter(state, "uninstall", "Could not detach the saved iPad filter.");
            output.WriteLine("Filter detach requested. Reconnect the iPad and run this command again to finish.");
            return;
        }
        if (bindings.Length > 0)
            throw new IOException("Reconnect the iPad before removing its driver files.");
        if (service is { Stopped: false })
            StopDriver();
        if (package is not null && system.Run(pnputil, "/delete-driver", package.PublishedName) != 0)
            throw new IOException("Removing the staged libusb0 package failed.");
        if (service is not null && system.Run(serviceControl, "delete", "libusb0") is not (0 or 1060))
            throw new IOException("Deleting the libusb0 service failed.");
        if (hasDriverFile)
            File.Delete(paths.DriverFile);
        if (hasJournal)
            File.Delete(paths.Journal);
        output.WriteLine($"Rollback complete. Verify Apple package {state.AppleInf} after reconnecting.");
    }

    private FilterReceipt? ReadRollback(string receiptPath, bool hasJournal, string? recoverPackage)
    {
        var receipt = FilterReceipt.Read(receiptPath);
        if (!hasJournal && receipt.InstallId is not null)
            return null;
        var state = hasJournal ? FilterReceipt.Read(paths.Journal) : receipt;
        if (hasJournal && (!Guid.TryParseExact(receipt.InstallId, "N", out _) || !Same(receipt.InstallId, state.InstallId)))
            throw new IOException("The rollback file does not match the current installation.");
        if (!hasJournal && (recoverPackage is null ||
                state.InstanceId?.StartsWith(@"USB\VID_05AC&PID_12A2\", StringComparison.OrdinalIgnoreCase) != true ||
                !Same(state.DriverPath, paths.DriverFile) || !Same(state.DriverHash, LibusbFiles.DriverHash)))
            throw new IOException("Earlier rollback files require an explicitly verified package name. See the recovery instructions.");
        if (state.InstanceId is null || !ChargingIpad.Matches(state.InstanceId) || !IsPublishedInf(state.AppleInf) ||
            state.LibusbInf is not null && !IsPublishedInf(state.LibusbInf))
            throw new IOException("Invalid rollback data for the selected iPad.");
        if (state.LibusbInf is not null && recoverPackage is not null && !Same(state.LibusbInf, recoverPackage))
            throw new IOException("The recovery package differs from the recorded package.");
        return state;
    }

    private void StopDriver()
    {
        if (system.Run(serviceControl, "stop", "libusb0") is not (0 or 1062))
            throw new IOException("Stopping libusb0 failed.");
        if (system.Service("libusb0") is { Stopped: false })
            throw new IOException("libusb0 is stopping. Run the rollback again.");
    }

    private DriverPackage? ResolvePackage(string? expected)
    {
        var packages = system.Packages();
        if (packages.Length > 1)
            throw new IOException("More than one libusb0 driver package is present.");
        if (packages is not [var package])
            return null;
        if (!MatchesPackage(package))
            throw new IOException("The Driver Store package does not match the tested libusb0 release.");
        if (expected is null)
            throw new IOException($"Verify {package.PublishedName} and use --recover-staged-package to finish interrupted staging.");
        if (!Same(expected, package.PublishedName))
            throw new IOException("The Driver Store package does not match the recorded installation.");
        return package;
    }

    private bool MatchesPackage(DriverPackage package) =>
        IsPublishedInf(package.PublishedName) && system.MatchesHash(package.InfPath, LibusbFiles.InfHash);

    private void RunFilter(FilterReceipt state, string action, string failure)
    {
        if (state.FilterTool is null || !Path.IsPathFullyQualified(state.FilterTool) ||
            !system.MatchesHash(state.FilterTool, LibusbFiles.ToolHash))
            throw new IOException("The filter installer does not match the tested release.");
        if (system.Run(state.FilterTool, action, "--device-id=" + state.InstanceId) != 0)
            throw new IOException(failure);
    }

    internal static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool IsPublishedInf(string? name) => name is not null && PublishedInf.IsMatch(name);
}
