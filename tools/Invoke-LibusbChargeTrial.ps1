#Requires -Version 5.1

[CmdletBinding(DefaultParameterSetName = 'SelfTest')]
param(
    [Parameter(Mandatory, ParameterSetName = 'CheckDevice')]
    [switch]$CheckDevice,

    [Parameter(Mandatory, ParameterSetName = 'ConfirmPort')]
    [switch]$ConfirmPort,

    [Parameter(Mandatory, ParameterSetName = 'ConfirmPort')]
    [ValidateRange(1, 2147483647)]
    [int]$PortMaxMa,

    [Parameter(Mandatory, ParameterSetName = 'Trial')]
    [switch]$RunOnce,

    [Parameter(Mandatory, ParameterSetName = 'Enable')]
    [switch]$Enable,

    [Parameter(ParameterSetName = 'Trial')]
    [Parameter(ParameterSetName = 'Enable')]
    [ValidateSet(1000, 1500, 2000)]
    [int]$LimitMa = 1000,

    [Parameter(ParameterSetName = 'Trial')]
    [ValidateRange(1, 180)]
    [int]$Seconds = 30,

    [Parameter(Mandatory, ParameterSetName = 'Restore')]
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'

function Get-UsbPortProfile([string]$InstanceId) {
    # Use Windows' full bus path to distinguish the same iPad on different ports or hubs.
    $location = Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName 'DEVPKEY_Device_LocationPaths' -ErrorAction SilentlyContinue
    $paths = @($location.Data |
        Where-Object { $_ -match '^(?:PCIROOT|ACPI|ACPI_HAL)\([A-Z0-9_]+\)(?:#(?:ACPI|PCI)\([A-Z0-9_]+\))*#USBROOT\(\d+\)(?:#USB\(\d+\))+$' } |
        ForEach-Object { $_.ToUpperInvariant() } | Sort-Object -Unique)
    if ($paths.Count -ne 1) { throw 'Cannot identify a single USB port path for the connected iPad.' }

    $path = $paths[0]
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $key = [BitConverter]::ToString($sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($path))).Replace('-', '')
    } finally {
        $sha256.Dispose()
    }
    $directory = Join-Path $env:LOCALAPPDATA 'LegacyIpadCharge\PortLimits'
    [pscustomobject]@{ LocationPath = $path; FilePath = (Join-Path $directory "$key.txt") }
}

function Save-UsbPortLimit([string]$FilePath, [int]$MaxMa) {
    $directory = [IO.Path]::GetDirectoryName($FilePath)
    $null = [IO.Directory]::CreateDirectory($directory)
    $temporaryFile = Join-Path $directory ([guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporaryFile, $MaxMa.ToString([Globalization.CultureInfo]::InvariantCulture))
        # Publish a complete value so a failed write cannot replace a previous limit.
        if ([IO.File]::Exists($FilePath)) {
            [IO.File]::Replace($temporaryFile, $FilePath, [System.Management.Automation.Language.NullString]::Value)
        } else {
            [IO.File]::Move($temporaryFile, $FilePath)
        }
    } finally {
        [IO.File]::Delete($temporaryFile)
    }
}

if (-not ('LegacyIpadChargeLibusb' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class LegacyIpadChargeLibusb
{
    private const uint VendorWrite = 0x222030;
    private const uint GetObjectName = 0x2223FC;
    private const int RequestSize = 24;

    private static byte[] ChargeRequest(uint activeOffsetMa)
    {
        // libusb0 takes vendor type 2. Apple takes the active offset in wIndex.
        var fields = new uint[] { 3000, 2, 0, 0x40, 0, activeOffsetMa };
        var request = new byte[RequestSize];
        Buffer.BlockCopy(fields, 0, request, 0, request.Length);
        return request;
    }

    public static void SelfTest()
    {
        const string Prefix = "B8-0B-00-00-02-00-00-00-00-00-00-00-40-00-00-00-00-00-00-00-";
        if (BitConverter.ToString(ChargeRequest(0)) != Prefix + "00-00-00-00" ||
            BitConverter.ToString(ChargeRequest(500)) != Prefix + "F4-01-00-00" ||
            BitConverter.ToString(ChargeRequest(1000)) != Prefix + "E8-03-00-00" ||
            BitConverter.ToString(ChargeRequest(1500)) != Prefix + "DC-05-00-00")
            throw new InvalidOperationException("Unexpected Apple charge request layout.");
    }

    public static SafeFileHandle OpenIpad(string instanceId)
    {
        var names = new char[65536];
        uint length = QueryDosDevice(null, names, (uint)names.Length);
        if (length == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Listing libusb0 links failed");

        var links = Array.FindAll(
            new string(names, 0, (int)length).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries),
            name => System.Text.RegularExpressions.Regex.IsMatch(name, @"\Alibusb0-[0-9]{4}\z"));
        if (links.Length != 1)
            throw new InvalidOperationException("Expected exactly one libusb0 device link.");

        var handle = CreateFile(@"\\.\" + links[0], 0xC0000000u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Opening the iPad filter failed");
        }

        try
        {
            // Read the filter's cached PnP registry path without sending a USB request.
            var request = new byte[RequestSize];
            var path = new byte[4096];
            uint lengthRead;
            if (!DeviceIoControl(handle, GetObjectName, request, (uint)request.Length,
                    path, (uint)path.Length, out lengthRead, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Reading filter identity failed");
            if (lengthRead < 2 || lengthRead >= path.Length || path[lengthRead - 1] != 0 ||
                !Encoding.ASCII.GetString(path, 0, (int)lengthRead - 1)
                    .EndsWith(@"\Enum\" + instanceId + @"\Device Parameters", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The libusb0 handle does not belong to the verified iPad.");
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public static void Send(SafeFileHandle handle, uint activeOffsetMa)
    {
        var request = ChargeRequest(activeOffsetMa);
        uint bytes;
        if (!DeviceIoControl(handle, VendorWrite, request, (uint)request.Length,
                null, 0, out bytes, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Apple vendor request failed");
        if (bytes != 0)
            throw new InvalidOperationException("Unexpected vendor response length: " + bytes);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string name, [Out] char[] target, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint sharing,
        IntPtr attributes, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input,
        uint inputLength, [Out] byte[] output, uint outputLength, out uint bytesReturned, IntPtr overlapped);
}
'@
}

[LegacyIpadChargeLibusb]::SelfTest()
if ($PSCmdlet.ParameterSetName -eq 'SelfTest') {
    'PASS 500, 1000, 1500 and 2000 mA request layouts. No USB requests sent.'
    return
}

if (-not ($CheckDevice -or $ConfirmPort -or $RunOnce -or $Enable -or $Restore)) {
    throw 'Specify an active command switch.'
}

if ($Enable -and -not $PSBoundParameters.ContainsKey('LimitMa')) {
    $LimitMa = 2000
}

if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit PowerShell.' }
$ipads = @(Get-PnpDevice -PresentOnly | Where-Object InstanceId -Match '^USB\\VID_05AC&PID_12A2\\')
if ($ipads.Count -ne 1 -or $ipads[0].Status -ne 'OK') {
    throw 'Expected exactly one ready iPad 2 with VID 05AC and PID 12A2.'
}
$instanceId = $ipads[0].InstanceId

$lockPath = Join-Path ([IO.Path]::GetTempPath()) 'LegacyIpadCharge-libusb.lock'
$restoreEventName = 'Local\LegacyIpadCharge-restore-' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$lockWait = [Diagnostics.Stopwatch]::StartNew()
while ($true) {
    try {
        $lock = [IO.FileStream]::new($lockPath, 'OpenOrCreate', 'ReadWrite', 'None', 1, 'DeleteOnClose')
        break
    } catch [IO.IOException] {
        if (-not $Restore) { throw }
        if ($lockWait.Elapsed.TotalSeconds -ge 15) {
            throw 'Could not complete restore while another operation was active. Retry or reconnect the iPad.'
        }
        $activeTrial = $null
        if ([Threading.EventWaitHandle]::TryOpenExisting($restoreEventName, [ref]$activeTrial)) {
            try { $null = $activeTrial.Set() }
            finally { $activeTrial.Dispose() }
        }
        [Threading.Thread]::Sleep(50)
    }
}
try {
    if ($ConfirmPort) {
        $profile = Get-UsbPortProfile $instanceId
        Save-UsbPortLimit $profile.FilePath $PortMaxMa
        "PORT_LIMIT_SAVED $PortMaxMa mA $($profile.LocationPath)"
        return
    }

    $driverPath = Join-Path $env:windir 'System32\drivers\libusb0.sys'
    $expectedHash = 'CDA0070FE4F7C5DE5D3BB06FA6D7E29E6F6740C0F2FA319FB5C1AF235D78D616'
    $driver = Get-CimInstance Win32_SystemDriver -Filter "Name='libusb0'"
    if ($driver.State -ne 'Running' -or $driver.PathName -ne $driverPath -or
        (Get-FileHash -LiteralPath $driverPath -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'Expected the running libusb-win32 1.4.0.2 x64 filter from the documented package.'
    }

    $properties = Get-PnpDeviceProperty -InstanceId $instanceId -KeyName @(
        'DEVPKEY_Device_Service', 'DEVPKEY_Device_UpperFilters'
    )
    if (($properties | Where-Object KeyName -like '*_Service').Data -ne 'usbccgp' -or
        ($properties | Where-Object KeyName -like '*_UpperFilters').Data -notcontains 'libusb0') {
        throw 'Expected libusb0 above the existing composite USB driver.'
    }

    $handle = [LegacyIpadChargeLibusb]::OpenIpad($instanceId)
    try {
        if ($CheckDevice) {
            'PASS libusb0 matches the connected iPad. No USB requests sent.'
            return
        }
        if ($Restore) {
            [LegacyIpadChargeLibusb]::Send($handle, 0)
            'RESTORE_OK ' + (Get-Date -Format o)
            return
        }

        $profile = Get-UsbPortProfile $instanceId
        if (-not [IO.File]::Exists($profile.FilePath)) {
            throw "No confirmed current limit for USB path $($profile.LocationPath). Verify its rating and run -ConfirmPort -PortMaxMa <mA>."
        }
        $confirmedLimitMa = 0
        $recordedLimit = [IO.File]::ReadAllText($profile.FilePath).Trim()
        if (-not [int]::TryParse($recordedLimit, [Globalization.NumberStyles]::None,
                [Globalization.CultureInfo]::InvariantCulture, [ref]$confirmedLimitMa) -or $confirmedLimitMa -lt 1) {
            throw "Invalid saved current limit for USB path $($profile.LocationPath). Verify its rating and run -ConfirmPort again."
        }
        if ($LimitMa -gt $confirmedLimitMa) {
            throw "Requested $LimitMa mA exceeds the confirmed limit of $confirmedLimitMa mA for USB path $($profile.LocationPath)."
        }

        $activeOffsetMa = [uint32]($LimitMa - 500)
        if ($Enable) {
            try {
                [LegacyIpadChargeLibusb]::Send($handle, $activeOffsetMa)
            } catch {
                try {
                    [LegacyIpadChargeLibusb]::Send($handle, 0)
                    'RESTORE_OK ' + (Get-Date -Format o)
                } catch {
                    Write-Warning 'Restore after the failed transfer also failed. Reconnect the iPad before retrying.'
                }
                throw
            }
            "ENABLE_OK $LimitMa mA " + (Get-Date -Format o)
            return
        }

        $created = $false
        $restoreSignal = [Threading.EventWaitHandle]::new($false, 'ManualReset', $restoreEventName, [ref]$created)
        try {
            if (-not $created) { throw 'A previous restore is still finishing. Retry the trial.' }
            try {
                if (-not $restoreSignal.WaitOne(0)) {
                    [LegacyIpadChargeLibusb]::Send($handle, $activeOffsetMa)
                    'RAISE_OK ' + (Get-Date -Format o)
                    $trialClock = [Diagnostics.Stopwatch]::StartNew()
                    while ($trialClock.Elapsed.TotalSeconds -lt $Seconds -and -not $restoreSignal.WaitOne(0)) {
                        Start-Sleep -Milliseconds 50
                    }
                }
            } finally {
                [LegacyIpadChargeLibusb]::Send($handle, 0)
                'RESTORE_OK ' + (Get-Date -Format o)
            }
        } finally { $restoreSignal.Dispose() }
    } finally {
        $handle.Dispose()
    }
} finally {
    $lock.Dispose()
}
