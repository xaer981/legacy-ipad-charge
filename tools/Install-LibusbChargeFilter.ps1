#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Bin
)

$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell.' }
. (Join-Path $PSScriptRoot 'LibusbCharge.InstallState.ps1')

# Load both entry points before any change to the driver stack.
& (Join-Path $PSScriptRoot 'Invoke-LibusbChargeTrial.ps1')
& (Join-Path $PSScriptRoot 'Remove-LibusbChargeFilter.ps1') -CheckEnvironment

$directory = (Resolve-Path -LiteralPath $Bin).ProviderPath
$filterTool = Join-Path $directory 'amd64\install-filter.exe'
$driverSource = Join-Path $directory 'amd64\libusb0.sys'
$infSource = Join-Path $directory 'libusb0.inf'
if ((Get-FileHash -LiteralPath $filterTool -Algorithm SHA256).Hash -ne $libusbToolHash -or
    (Get-FileHash -LiteralPath $driverSource -Algorithm SHA256).Hash -ne $libusbDriverHash -or
    (Get-FileHash -LiteralPath $infSource -Algorithm SHA256).Hash -ne $libusbInfHash) {
    throw 'Use the unmodified libusb-win32 1.4.0.2 package from the documented release.'
}

$lock = Enter-LibusbInstallLock
try {
    if ([IO.File]::Exists($libusbJournalPath)) {
        $active = Import-Clixml -LiteralPath $libusbJournalPath
        throw "An installation is already recorded. Use its rollback file: $($active.RollbackFile)"
    }
    if ((Get-CimInstance Win32_SystemDriver -Filter "Name='libusb0' OR Name='libusbd'") -or
        [IO.File]::Exists($libusbDriverPath) -or (Get-LibusbPackages)) {
        throw 'libusb0 is already present. Use the rollback instructions for that installation.'
    }

    $ipads = @(Get-PnpDevice -PresentOnly | Where-Object InstanceId -Match '^USB\\VID_05AC&PID_12A2\\[^\\\s]+$')
    if ($ipads.Count -ne 1 -or $ipads[0].Status -ne 'OK') { throw 'Expected exactly one ready iPad 2 GSM.' }
    $instanceId = $ipads[0].InstanceId
    $properties = Get-PnpDeviceProperty -InstanceId $instanceId -KeyName @(
        'DEVPKEY_Device_Service', 'DEVPKEY_Device_DriverInfPath'
    )
    $appleInf = ($properties | Where-Object KeyName -EQ 'DEVPKEY_Device_DriverInfPath').Data
    if (($properties | Where-Object KeyName -EQ 'DEVPKEY_Device_Service').Data -ne 'usbccgp' -or
        $appleInf -notmatch '^oem\d+\.inf$') { throw 'Expected the original Apple composite USB stack.' }

    $id = [guid]::NewGuid().ToString('N')
    $backup = Join-Path $env:LOCALAPPDATA "LegacyIpadCharge\Backups\$id"
    $null = [IO.Directory]::CreateDirectory($backup)
    pnputil /export-driver $appleInf $backup
    if ($LASTEXITCODE -ne 0) { throw 'Apple package backup failed.' }
    $rollbackFile = Join-Path $backup 'libusb-rollback.clixml'
    [pscustomobject]@{ InstallId = $id } | Export-Clixml -LiteralPath $rollbackFile
    $installation = [pscustomobject]@{
        InstallId = $id
        InstanceId = $instanceId
        AppleInf = $appleInf
        FilterTool = $filterTool
        LibusbInf = $null
        RollbackFile = $rollbackFile
    }
    Save-LibusbInstallJournal $installation
    "Rollback file: $rollbackFile"

    pnputil /add-driver $infSource
    if ($LASTEXITCODE -ne 0) { throw 'Staging libusb0 failed. Use the saved rollback file.' }
    $staged = @(Get-LibusbPackages)
    if ($staged.Count -ne 1 -or $staged[0].Driver -notmatch '^oem\d+\.inf$' -or
        (Get-FileHash -LiteralPath $staged[0].OriginalFileName -Algorithm SHA256).Hash -ne $libusbInfHash) {
        throw 'Could not identify the staged libusb0 package. Use the saved rollback file.'
    }
    $installation.LibusbInf = $staged[0].Driver
    Save-LibusbInstallJournal $installation

    [IO.File]::Copy($driverSource, $libusbDriverPath)
    & $filterTool install "--device-id=$instanceId"
    if ($LASTEXITCODE -ne 0) { throw 'Filter installation failed. Use the saved rollback file.' }
    'Filter installation requested. Reconnect the iPad and run -CheckDevice before charging.'
} finally {
    $lock.Dispose()
}
