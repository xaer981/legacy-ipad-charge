#Requires -Version 5.1
[CmdletBinding()]
param([string]$Dotnet = 'dotnet')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root 'bin\release'
$stage = Join-Path $release ('package-' + [guid]::NewGuid().ToString('N'))
$content = Join-Path $stage 'content'
$published = Join-Path $stage 'publish'
$assetName = 'legacy-ipad-charge-windows-x64.zip'
$archive = Join-Path $release $assetName
Add-Type -AssemblyName System.IO.Compression.FileSystem
$null = [IO.Directory]::CreateDirectory($content)
Push-Location -LiteralPath $root
try {
    $publishOptions = @(
        'publish', 'src/LegacyIpadCharge', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true'
        '-p:Platform=AnyCPU', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true'
        '-p:DebugType=None', '-o', $published, '-warnaserror'
    )
    & $Dotnet @publishOptions
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the Windows CLI failed.' }

    Copy-Item -LiteralPath (Join-Path $published 'legacy-ipad-charge.exe') -Destination $content
    Copy-Item -LiteralPath 'QUICKSTART.md' -Destination (Join-Path $content 'README.md')
    foreach ($file in @(
        'LICENSE'
        'tools/Install-LibusbChargeFilter.ps1'
        'tools/Invoke-LibusbChargeTrial.ps1'
        'tools/Remove-LibusbChargeFilter.ps1'
        'tools/LibusbCharge.InstallState.ps1'
        'docs/libusb-charge.md'
        'docs/compatibility.md'
        'docs/diagnostics.md'
        'docs/feasibility.md'
    )) {
        $destination = Join-Path $content $file
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-Item -LiteralPath $file -Destination $destination
    }

    $assets = Get-Content -LiteralPath 'src/LegacyIpadCharge/obj/project.assets.json' -Raw -Encoding UTF8 | ConvertFrom-Json
    $runtime = $assets.project.frameworks.'net10.0'.downloadDependencies |
        Where-Object name -EQ 'Microsoft.NETCore.App.Runtime.win-x64'
    if ($runtime.version -notmatch '^\[([^,\s]+),\s*\1\]$') {
        throw 'Could not identify the exact published .NET runtime pack.'
    }
    $runtimeVersion = $Matches[1]
    $runtimePack = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
        Join-Path $_ "microsoft.netcore.app.runtime.win-x64\$runtimeVersion"
    } | Where-Object { [IO.Directory]::Exists($_) } | Select-Object -First 1
    if (-not $runtimePack) { throw 'Missing the published runtime pack and its notices.' }
    Copy-Item -LiteralPath (Join-Path $runtimePack 'LICENSE.TXT') -Destination (Join-Path $content 'dotnet-LICENSE.txt')
    $notices = Join-Path $runtimePack 'THIRD-PARTY-NOTICES.TXT'
    Copy-Item -LiteralPath $notices -Destination (Join-Path $content 'dotnet-THIRD-PARTY-NOTICES.txt')

    $temporaryZip = Join-Path $stage $assetName
    [IO.Compression.ZipFile]::CreateFromDirectory($content, $temporaryZip, 'Optimal', $false)
    if ([IO.File]::Exists($archive)) {
        [IO.File]::Replace($temporaryZip, $archive, [System.Management.Automation.Language.NullString]::Value)
    } else {
        [IO.File]::Move($temporaryZip, $archive)
    }
    Get-FileHash -LiteralPath $archive -Algorithm SHA256
} finally {
    Pop-Location
    $resolvedStage = (Resolve-Path -LiteralPath $stage).ProviderPath
    $resolvedRelease = (Resolve-Path -LiteralPath $release).ProviderPath
    if ([IO.Directory]::GetParent($resolvedStage).FullName -eq $resolvedRelease -and
        [IO.Path]::GetFileName($resolvedStage) -match '^package-[0-9a-f]{32}$') {
        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
