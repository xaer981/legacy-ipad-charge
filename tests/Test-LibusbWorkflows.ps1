#Requires -Version 5.1
param([string]$RestoreWorkerRoot)

$ErrorActionPreference = 'Stop'

# The production script reuses this type. No test calls a native USB function.
if ('LegacyIpadChargeLibusb' -as [type]) { throw 'Run the mocked tests in a fresh PowerShell process.' }
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using Microsoft.Win32.SafeHandles;

public static class LegacyIpadChargeLibusb
{
    public static readonly List<uint> Requests = new List<uint>();
    public static void SelfTest() { }
    public static SafeFileHandle OpenIpad(string id) { return new SafeFileHandle(IntPtr.Zero, false); }
    public static void Send(SafeFileHandle handle, uint offset) { Requests.Add(offset); }
}
'@

$repo = Split-Path -Parent $PSScriptRoot
$testScriptPath = $PSCommandPath
$charge = Join-Path $repo 'tools\Invoke-LibusbChargeTrial.ps1'
$remove = Join-Path $repo 'tools\Remove-LibusbChargeFilter.ps1'
$install = Join-Path $repo 'tools\Install-LibusbChargeFilter.ps1'
$instanceId = 'USB\VID_05AC&PID_12A2\MOCK'
$driverHash = 'CDA0070FE4F7C5DE5D3BB06FA6D7E29E6F6740C0F2FA319FB5C1AF235D78D616'
$infHash = '62ABAB852A7FE61CFC9490A57908ED2793F3681575FABFB52987FEB44FD51AA3'
$toolHash = '95F4F575639B9A4BF4F8A2F2EE226720CF87FB962AA33B6327EF0C704B63A3C4'
$writes = [System.Collections.Generic.List[string]]::new()
$firstPort = 'PCIROOT(80)#PCI(1400)#USBROOT(0)#USB(2)'
$secondPort = 'PCIROOT(80)#PCI(1400)#USBROOT(0)#USB(2)#USB(1)'
$fake = @{ Binding = $false; Stack = $false; ServicePresent = $false; ServiceState = ''; DriverPresent = $false
    PackagePresent = $false; StagedHash = ''; TrialOffset = 0; LocationPaths = @($firstPort); InterruptStage = $false
    StartRestoreWorker = $false; RestoreWorker = $null }

function Assert-Equal($actual, $expected) {
    if ($actual -cne $expected) { throw "Expected '$expected', got '$actual'." }
}
function Assert-ChargingBlocked($parameters, $errorPattern) {
    try {
        $null = & $charge @parameters
        throw 'Charging was not blocked.'
    } catch {
        if ($_.Exception.Message -notmatch $errorPattern) { throw }
    }
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests.Count) 0
}
function Get-PnpDevice { param([switch]$PresentOnly) [pscustomobject]@{ InstanceId = $instanceId; Status = 'OK' } }
function Get-PnpDeviceProperty {
    [CmdletBinding()]
    param($InstanceId, $KeyName)
    if ($KeyName -contains 'DEVPKEY_Device_LocationPaths') {
        [pscustomobject]@{ InstanceId = $InstanceId; KeyName = 'DEVPKEY_Device_LocationPaths'; Data = $fake.LocationPaths }
        return
    }
    if ($KeyName -contains 'DEVPKEY_Device_Service') {
        [pscustomobject]@{ InstanceId = $InstanceId; KeyName = 'DEVPKEY_Device_Service'; Data = 'usbccgp' }
    }
    if ($KeyName -contains 'DEVPKEY_Device_DriverInfPath') {
        [pscustomobject]@{ InstanceId = $InstanceId; KeyName = 'DEVPKEY_Device_DriverInfPath'; Data = 'oem11.inf' }
    }
    if ($fake.Binding) {
        [pscustomobject]@{ InstanceId = $InstanceId; KeyName = 'DEVPKEY_Device_UpperFilters'; Data = @('libusb0') }
    }
    if ($fake.Stack) {
        [pscustomobject]@{ InstanceId = $InstanceId; KeyName = 'DEVPKEY_Device_Stack'; Data = @('\Driver\libusb0') }
    }
}
function Get-Process { throw 'Charging must not inspect host applications.' }
function Get-NetTCPConnection { throw 'Charging must not inspect TCP connections.' }
function Start-Sleep {
    param([int]$Milliseconds)
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests -join ',') ([string]$fake.TrialOffset)
    if ($fake.StartRestoreWorker -and -not $fake.RestoreWorker) {
        $fake.RestoreWorker = Start-Job -ScriptBlock {
            param($Script, $Root)
            & $Script -RestoreWorkerRoot $Root
        } -ArgumentList $testScriptPath, $testAppData
    }
    Microsoft.PowerShell.Utility\Start-Sleep -Milliseconds $Milliseconds
}
function Get-CimInstance {
    param($ClassName, $Filter)
    if ($fake.ServicePresent) { [pscustomobject]@{ Name = 'libusb0'; State = $fake.ServiceState; PathName = $driverPath } }
}
function Get-WindowsDriver {
    param([switch]$Online)
    if ($fake.PackagePresent) { [pscustomobject]@{ Driver = 'oem42.inf'; OriginalFileName = 'Z:\mock\libusb0.inf' } }
}
function Get-FileHash {
    param($LiteralPath, $Algorithm)
    if ($LiteralPath -eq $driverPath -and $fake.DriverPresent) { return @{ Hash = $driverHash } }
    if ($LiteralPath -eq 'Z:\mock\libusb0.inf') { return @{ Hash = $fake.StagedHash } }
    if ($LiteralPath -eq (Join-Path $fakeBin 'libusb0.inf')) { return @{ Hash = $infHash } }
    if ($LiteralPath -eq (Join-Path $fakeBin 'amd64\libusb0.sys')) { return @{ Hash = $driverHash } }
    if ($LiteralPath -in @('Test-DetachFilter', (Join-Path $fakeBin 'amd64\install-filter.exe'))) {
        return @{ Hash = $toolHash }
    }
    throw "Unexpected hash request: $LiteralPath"
}
function Get-ChildItem { param($LiteralPath) }
function Test-Path {
    param($LiteralPath, $PathType)
    Assert-Equal $LiteralPath $driverPath
    $fake.DriverPresent
}
function Test-DetachFilter {
    param($Action, $Target)
    Assert-Equal "$Action $Target" "uninstall --device-id=$instanceId"
    $writes.Add('detach')
    $fake.Binding = $false
    $global:LASTEXITCODE = 0
}
function pnputil {
    param($Action, $Package)
    switch ($Action) {
        '/export-driver' {
            Assert-Equal $Package 'oem11.inf'
            $writes.Add('backup')
        }
        '/add-driver' {
            Assert-Equal $Package (Join-Path $fakeBin 'libusb0.inf')
            if (-not [IO.File]::Exists($journalPath)) { throw 'Missing journal before staging.' }
            $writes.Add('stage')
            $fake.PackagePresent = $true
            $fake.StagedHash = $infHash
            if ($fake.InterruptStage) { throw 'Simulated interruption after staging.' }
        }
        '/delete-driver' {
            Assert-Equal $Package 'oem42.inf'
            $writes.Add('package')
            $fake.PackagePresent = $false
        }
        default { throw "Unexpected PnPUtil action: $Action" }
    }
    $global:LASTEXITCODE = 0
}
function sc.exe {
    param($Action, $Service)
    Assert-Equal $Service 'libusb0'
    Assert-Equal $Action 'delete'
    $writes.Add('service')
    $fake.ServicePresent = $false
    $global:LASTEXITCODE = 0
}
function Remove-Item {
    param($LiteralPath)
    Assert-Equal $LiteralPath $driverPath
    $writes.Add('file')
    $fake.DriverPresent = $false
}

if ($RestoreWorkerRoot) {
    $env:windir = Join-Path $RestoreWorkerRoot 'windows'
    $env:LOCALAPPDATA = $RestoreWorkerRoot
    $driverPath = Join-Path $env:windir 'System32\drivers\libusb0.sys'
    $fakeBin = Join-Path $RestoreWorkerRoot 'source\bin'
    $fake.Binding = $fake.ServicePresent = $fake.DriverPresent = $true
    $fake.ServiceState = 'Running'
    & $charge -Restore
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests -join ',') '0'
    return
}

$originalWindows = $env:windir
$originalLocalAppData = $env:LOCALAPPDATA
$testAppData = Join-Path ([IO.Path]::GetTempPath()) ('fake-ipad-appdata-' + [guid]::NewGuid())
try {
    $env:windir = Join-Path $testAppData 'windows'
    $env:LOCALAPPDATA = $testAppData
    $fakeBin = Join-Path $testAppData 'source\bin'
    $null = [IO.Directory]::CreateDirectory($fakeBin)
    $driverPath = Join-Path $env:windir 'System32\drivers\libusb0.sys'
    $journalPath = Join-Path $env:windir 'System32\drivers\LegacyIpadCharge-libusb.clixml'
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($journalPath))
    $receiptPath = Join-Path $testAppData 'rollback.clixml'
    $oldReceiptPath = Join-Path $testAppData 'old-rollback.clixml'
    $saved = [pscustomobject]@{ InstanceId = $instanceId; AppleInf = 'oem11.inf'; DriverPath = $driverPath
        FilterTool = 'Test-DetachFilter'
        DriverHash = $driverHash; LibusbInf = $null; InstallId = [guid]::NewGuid().ToString('N') }
    [pscustomobject]@{ InstallId = $saved.InstallId } | Export-Clixml -LiteralPath $receiptPath
    [pscustomobject]@{ InstallId = [guid]::NewGuid().ToString('N') } | Export-Clixml -LiteralPath $oldReceiptPath
    $fake.Binding = $fake.ServicePresent = $fake.DriverPresent = $true
    $fake.ServiceState = 'Running'

    Assert-ChargingBlocked @{ Enable = $true } 'No confirmed current limit'
    Assert-ChargingBlocked @{ RunOnce = $true; Seconds = 1 } 'No confirmed current limit'
    $fake.LocationPaths = @()
    Assert-ChargingBlocked @{ Enable = $true } 'Cannot identify a single USB port path'
    $result = & $charge -Restore
    Assert-Equal ($result -match '^RESTORE_OK ') $true
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests -join ',') '0'
    [LegacyIpadChargeLibusb]::Requests.Clear()
    'PASS unknown USB paths block charging but allow restore'

    $fake.LocationPaths = @($firstPort, 'ACPI(_SB_)#ACPI(XHCI)#ACPI(RHUB)#ACPI(HS02)')
    $result = & $charge -ConfirmPort -PortMaxMa 1500
    Assert-Equal ($result -like "PORT_LIMIT_SAVED 1500 mA $firstPort") $true
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests.Count) 0
    Assert-ChargingBlocked @{ Enable = $true } 'exceeds the confirmed limit of 1500 mA'
    Assert-ChargingBlocked @{ RunOnce = $true; LimitMa = 2000; Seconds = 1 } 'exceeds the confirmed limit of 1500 mA'
    $savedLimits = [IO.Directory]::GetFiles((Join-Path $testAppData 'LegacyIpadCharge\PortLimits'), '*.txt')
    Assert-Equal $savedLimits.Count 1
    [IO.File]::WriteAllText($savedLimits[0], 'invalid')
    Assert-ChargingBlocked @{ Enable = $true; LimitMa = 1000 } 'Invalid saved current limit'
    $null = & $charge -Restore
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests -join ',') '0'
    [LegacyIpadChargeLibusb]::Requests.Clear()
    $null = & $charge -ConfirmPort -PortMaxMa 1500
    'PASS invalid saved limits block charging but allow restore'

    $fake.LocationPaths = @($secondPort)
    Assert-ChargingBlocked @{ Enable = $true; LimitMa = 1000 } 'No confirmed current limit'
    $null = & $charge -ConfirmPort -PortMaxMa 1000
    Assert-ChargingBlocked @{ Enable = $true; LimitMa = 1500 } 'exceeds the confirmed limit of 1000 mA'

    $fake.LocationPaths = @($firstPort.ToLowerInvariant())
    Assert-ChargingBlocked @{ Enable = $true } 'exceeds the confirmed limit of 1500 mA'
    $null = & $charge -ConfirmPort -PortMaxMa 2000
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests.Count) 0
    'PASS confirmed limits are isolated by USB path and can be updated'

    $result = & $charge -Enable
    Assert-Equal ($result -match '^ENABLE_OK 2000 mA ') $true
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests -join ',') '1500'
    $null = & $charge -Restore
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests -join ',') '1500,0'
    'PASS enable defaults to 2000 mA and manual restore returns to 500 mA'

    foreach ($limit in @(1000, 1500, 2000)) {
        [LegacyIpadChargeLibusb]::Requests.Clear()
        $result = & $charge -Enable -LimitMa $limit
        Assert-Equal ($result -match "^ENABLE_OK $limit mA ") $true
        Assert-Equal ([LegacyIpadChargeLibusb]::Requests -join ',') ([string]($limit - 500))
    }
    [LegacyIpadChargeLibusb]::Requests.Clear()
    try {
        $null = & $charge -Enable -LimitMa 2100
        throw 'Charging accepted an unsupported limit.'
    } catch [System.Management.Automation.ParameterBindingException] {
        Assert-Equal $_.Exception.ParameterName 'LimitMa'
    }
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests.Count) 0
    'PASS explicit current choices and rejection of unsupported limits'

    $fake.TrialOffset = 500
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $null = & $charge -RunOnce -Seconds 1
    Assert-Equal ($clock.Elapsed.TotalSeconds -ge 1) $true
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests -join ',') '500,0'
    [LegacyIpadChargeLibusb]::Requests.Clear()
    $fake.TrialOffset = 1500
    $null = & $charge -RunOnce -LimitMa 2000 -Seconds 1
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests -join ',') '1500,0'
    'PASS timed trial starts at 1000 mA by default and restores after 2000 mA'
    [LegacyIpadChargeLibusb]::Requests.Clear()
    $fake.StartRestoreWorker = $true
    $clock.Restart()
    $null = & $charge -RunOnce -LimitMa 2000 -Seconds 45
    Assert-Equal ($clock.Elapsed.TotalSeconds -lt 35) $true
    if (-not (Wait-Job -Job $fake.RestoreWorker -Timeout 20)) { throw 'Restore worker timed out.' }
    $workerOutput = Receive-Job -Job $fake.RestoreWorker -ErrorAction Stop
    Assert-Equal $fake.RestoreWorker.State 'Completed'
    Assert-Equal ($workerOutput -match '^RESTORE_OK ') $true
    Assert-Equal ([LegacyIpadChargeLibusb]::Requests -join ',') '1500,0'
    'PASS manual restore ends the active trial in another PowerShell process'

    $fake.Binding = $fake.ServicePresent = $fake.DriverPresent = $false
    $fake.InterruptStage = $true
    try { $null = & $install -Bin $fakeBin; throw 'Installation did not stop at the simulated interruption.' }
    catch { Assert-Equal $_.Exception.Message 'Simulated interruption after staging.' }
    Assert-Equal ($writes -join ',') 'backup,stage'
    $interrupted = Import-Clixml -LiteralPath $journalPath
    Assert-Equal $interrupted.LibusbInf $null
    try { & $remove -RollbackFile $interrupted.RollbackFile; throw 'Recovery removed an unrecorded package.' }
    catch { Assert-Equal ($_.Exception.Message -like 'Staging was interrupted*') $true }
    try { & $remove -RollbackFile $interrupted.RollbackFile -RecoverStagedPackage oem43.inf; throw 'Recovery accepted another package.' }
    catch { Assert-Equal $_.Exception.Message 'The Driver Store package does not match the recorded installation.' }
    Assert-Equal ($writes -join ',') 'backup,stage'
    $null = & $remove -RollbackFile $interrupted.RollbackFile -RecoverStagedPackage oem42.inf
    $null = & $remove -RollbackFile $interrupted.RollbackFile
    Assert-Equal ($writes -join ',') 'backup,stage,package'
    'PASS interrupted staging needs explicit recovery and can be rolled back twice'

    $fake.InterruptStage = $false
    $writes.Clear()
    try { $null = & $install -Bin $fakeBin; throw 'Installation opened a missing driver file.' }
    catch { Assert-Equal ($_.Exception.InnerException -is [IO.DirectoryNotFoundException]) $true }
    Assert-Equal ($writes -join ',') 'backup,stage'
    $recorded = Import-Clixml -LiteralPath $journalPath
    Assert-Equal $recorded.LibusbInf 'oem42.inf'
    $null = & $remove -RollbackFile $recorded.RollbackFile
    Assert-Equal ($writes -join ',') 'backup,stage,package'
    'PASS the package name is durable before copying or attaching the driver'

    $fake.PackagePresent = $true
    $fake.StagedHash = 'unrelated package'
    $writes.Clear()
    $saved.LibusbInf = 'oem42.inf'
    $saved | Export-Clixml -LiteralPath $journalPath
    try {
        & $remove -RollbackFile $receiptPath
        throw 'Rollback accepted a different package.'
    } catch {
        Assert-Equal $_.Exception.Message 'The Driver Store package does not match the tested libusb0 release.'
    }
    Assert-Equal $writes.Count 0
    'PASS rollback leaves an unrelated Driver Store package alone'

    $fake.StagedHash = $infHash
    try { & $remove -RollbackFile $oldReceiptPath; throw 'Rollback accepted the previous receipt.' }
    catch { Assert-Equal $_.Exception.Message 'The rollback file does not match the current installation.' }
    Assert-Equal $writes.Count 0
    'PASS old rollback receipts cannot remove a newer matching package'

    $fake.PackagePresent = $fake.ServicePresent = $fake.DriverPresent = $true
    $fake.StagedHash = $infHash
    $fake.ServiceState = 'Stopped'
    $fake.Binding = $fake.Stack = $true
    $writes.Clear()
    $null = & $remove -RollbackFile $receiptPath
    Assert-Equal ($writes -join ',') 'detach'
    try {
        & $remove -RollbackFile $receiptPath
        throw 'Rollback cleaned up an active filter.'
    } catch {
        Assert-Equal $_.Exception.Message 'Reconnect the iPad before removing its driver files.'
    }
    Assert-Equal ($writes -join ',') 'detach'
    $fake.Stack = $false
    $null = & $remove -RollbackFile $receiptPath
    Assert-Equal ($writes -join ',') 'detach,package,service,file'
    'PASS rollback waits for filter detach and then removes owned artifacts'
    Assert-Equal ([IO.File]::Exists($journalPath)) $false

    $legacyPath = Join-Path $testAppData 'legacy.clixml'
    $saved.PSObject.Properties.Remove('InstallId')
    $saved.LibusbInf = $null
    $saved | Export-Clixml -LiteralPath $legacyPath
    $fake.PackagePresent = $true
    $writes.Clear()
    try { & $remove -RollbackFile $legacyPath; throw 'Legacy rollback guessed a package.' }
    catch { Assert-Equal ($_.Exception.Message -like 'Earlier rollback files*') $true }
    Assert-Equal $writes.Count 0
    $null = & $remove -RollbackFile $legacyPath -RecoverStagedPackage oem42.inf
    Assert-Equal ($writes -join ',') 'package'
    'PASS legacy rollback requires the confirmed package'
} finally {
    if ($fake.RestoreWorker) { Remove-Job -Job $fake.RestoreWorker -Force }
    $env:windir = $originalWindows
    $env:LOCALAPPDATA = $originalLocalAppData
    if ([IO.Directory]::Exists($testAppData) -and
        [IO.Directory]::GetParent($testAppData).FullName -eq ([IO.Path]::GetTempPath()).TrimEnd('\')) {
        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $testAppData -Recurse -Force
    }
}
