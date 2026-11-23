#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Rollback')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Rollback')]
    [string]$RollbackFile,

    [Parameter(ParameterSetName = 'Rollback')]
    [ValidatePattern('^oem\d+\.inf$')]
    [string]$RecoverStagedPackage,

    [Parameter(Mandatory, ParameterSetName = 'CheckEnvironment')]
    [switch]$CheckEnvironment
)

$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell.' }
. (Join-Path $PSScriptRoot 'LibusbCharge.InstallState.ps1')
if ($PSCmdlet.ParameterSetName -eq 'CheckEnvironment') {
    'PASS rollback script loaded. No driver changes.'
    return
}

$lock = Enter-LibusbInstallLock
$chargeLock = $null
try {
    $receipt = Import-Clixml -LiteralPath $RollbackFile
    $hasJournal = [IO.File]::Exists($libusbJournalPath)
    if ($hasJournal) {
        $rollback = Import-Clixml -LiteralPath $libusbJournalPath
        if ($receipt.InstallId -notmatch '^[0-9a-f]{32}$' -or $receipt.InstallId -ne $rollback.InstallId) {
            throw 'The rollback file does not match the current installation.'
        }
    } elseif ($receipt.InstallId) {
        'No active installation matches this rollback file.'
        return
    } else {
        $rollback = $receipt
        if (-not $RecoverStagedPackage -or $rollback.DriverPath -ne $libusbDriverPath -or
            $rollback.DriverHash -ne $libusbDriverHash) {
            throw 'Earlier rollback files require an explicitly verified package name. See the recovery instructions.'
        }
    }
    if ($rollback.InstanceId -notmatch '^USB\\VID_05AC&PID_12A2\\[^\\\s]+$' -or
        $rollback.AppleInf -notmatch '^oem\d+\.inf$' -or
        ($rollback.LibusbInf -and $rollback.LibusbInf -notmatch '^oem\d+\.inf$')) {
        throw 'Expected a rollback file created by this project for an iPad 2 GSM.'
    }
    if ($rollback.LibusbInf -and $RecoverStagedPackage -and $rollback.LibusbInf -ne $RecoverStagedPackage) {
        throw 'The recovery package differs from the recorded package.'
    }

    $chargeLockPath = Join-Path ([IO.Path]::GetTempPath()) 'LegacyIpadCharge-libusb.lock'
    $chargeLock = [IO.FileStream]::new($chargeLockPath, 'OpenOrCreate', 'ReadWrite', 'None', 1, 'DeleteOnClose')
    $packages = @(Get-LibusbPackages)
    if ($packages.Count -gt 1) { throw 'More than one libusb0 driver package is present.' }
    $package = $packages | Select-Object -First 1
    if ($package -and ($package.Driver -notmatch '^oem\d+\.inf$' -or
            (Get-FileHash -LiteralPath $package.OriginalFileName -Algorithm SHA256).Hash -ne $libusbInfHash)) {
        throw 'The Driver Store package does not match the tested libusb0 release.'
    }
    $expectedPackage = $rollback.LibusbInf
    if (-not $expectedPackage) { $expectedPackage = $RecoverStagedPackage }
    if ($package -and -not $expectedPackage) {
        throw "Staging was interrupted before the package name was recorded. Verify $($package.Driver) and use -RecoverStagedPackage."
    }
    if ($package -and $expectedPackage -ne $package.Driver) {
        throw 'The Driver Store package does not match the recorded installation.'
    }

    $hasFile = Test-Path -LiteralPath $libusbDriverPath -PathType Leaf
    if ($hasFile -and (Get-FileHash -LiteralPath $libusbDriverPath -Algorithm SHA256).Hash -ne $libusbDriverHash) {
        throw 'The installed libusb0.sys does not match the tested release.'
    }

    $services = @(Get-CimInstance Win32_SystemDriver -Filter "Name='libusb0' OR Name='libusbd'")
    $service = $services | Where-Object Name -EQ 'libusb0'
    if (($services | Where-Object Name -EQ 'libusbd') -or ($service -and $service.PathName -ne $libusbDriverPath)) {
        throw 'The existing libusb service is outside this rollback.'
    }

    $classFilters = @(Get-ChildItem -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Class' |
        Get-ItemProperty | Where-Object { $_.UpperFilters -contains 'libusb0' -or $_.LowerFilters -contains 'libusb0' })
    if ($classFilters.Count) { throw 'A device class still uses libusb0.' }

    $usbDevices = @(Get-PnpDevice | Where-Object InstanceId -Like 'USB\*')
    $filterReferences = @(if ($usbDevices.Count) {
        Get-PnpDeviceProperty -InstanceId $usbDevices.InstanceId -KeyName @(
            'DEVPKEY_Device_UpperFilters', 'DEVPKEY_Device_LowerFilters', 'DEVPKEY_Device_Service', 'DEVPKEY_Device_Stack'
        ) -ErrorAction SilentlyContinue | Where-Object {
            $_.Data -contains 'libusb0' -or $_.Data -contains '\Driver\libusb0'
        }
    })
    if ($filterReferences | Where-Object {
        $_.InstanceId -ne $rollback.InstanceId -or $_.KeyName -notin @('DEVPKEY_Device_UpperFilters', 'DEVPKEY_Device_Stack')
    }) { throw 'Another USB driver binding still uses libusb0.' }

    if (-not $expectedPackage -and ($hasFile -or $service -or $filterReferences.Count)) {
        throw 'Driver components exist without a recorded package. Verify the installation before continuing.'
    }
    if ($hasJournal -and -not $rollback.LibusbInf -and $package) {
        $rollback.LibusbInf = $expectedPackage
        Save-LibusbInstallJournal $rollback
    }

    if ($filterReferences | Where-Object KeyName -EQ 'DEVPKEY_Device_UpperFilters') {
        if ((Get-FileHash -LiteralPath $rollback.FilterTool -Algorithm SHA256).Hash -ne $libusbToolHash) {
            throw 'The filter installer does not match the tested release.'
        }
        & $rollback.FilterTool uninstall "--device-id=$($rollback.InstanceId)"
        if ($LASTEXITCODE -ne 0) { throw 'Could not detach the saved iPad filter.' }
        'Filter detach requested. Reconnect the iPad and run this command again to finish.'
        return
    }
    if ($filterReferences.Count) { throw 'Reconnect the iPad before removing its driver files.' }

    if ($service -and $service.State -ne 'Stopped') {
        sc.exe stop libusb0
        if ($LASTEXITCODE -notin @(0,1062)) { throw 'Stopping libusb0 failed.' }
        $service = Get-CimInstance Win32_SystemDriver -Filter "Name='libusb0'"
        if ($service -and $service.State -ne 'Stopped') { throw 'libusb0 is stopping. Run the rollback again.' }
    }
    if ($package) {
        pnputil /delete-driver $package.Driver
        if ($LASTEXITCODE -ne 0) { throw 'Removing the staged libusb0 package failed.' }
    }
    if ($service) {
        sc.exe delete libusb0
        if ($LASTEXITCODE -notin @(0,1060)) { throw 'Deleting the libusb0 service failed.' }
    }
    if ($hasFile) { Remove-Item -LiteralPath $libusbDriverPath }
    if ($hasJournal) { [IO.File]::Delete($libusbJournalPath) }

    "Rollback complete. Verify the iPad is recognized with Apple package $($rollback.AppleInf) after reconnecting."
} finally {
    if ($chargeLock) { $chargeLock.Dispose() }
    $lock.Dispose()
}
