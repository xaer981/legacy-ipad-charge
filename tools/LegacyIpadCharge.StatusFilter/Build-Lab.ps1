param(
    [string]$Tools = (Join-Path $env:LOCALAPPDATA 'legacy-ipad-charge-tools\nuget\10.0.26100.6584'),
    [switch]$LabWithoutSpectreLibraries
)

$ErrorActionPreference = 'Stop'
$packageVersion = '10.0.26100.6584'
$kitVersion = '10.0.26100.0'
$wdk = Join-Path $Tools "microsoft.windows.wdk.x64.$packageVersion\c"
$sdk = Join-Path $Tools "microsoft.windows.sdk.cpp.$packageVersion\c"
$sdkX64 = Join-Path $Tools "microsoft.windows.sdk.cpp.x64.$packageVersion\c"
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Visual Studio C++ build tools not found.' }
$toolset = (Get-Content (Join-Path $vs 'VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
$vc = Join-Path $vs "VC\Tools\MSVC\$toolset"
$cl = Join-Path $vc 'bin\Hostx64\x64\cl.exe'
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\amd64\MSBuild.exe'
$sign = Join-Path $sdk "bin\$kitVersion\x64\signtool.exe"
$catalog = Join-Path $wdk "bin\$kitVersion\x86\Inf2Cat.exe"
$infverif = Join-Path $wdk "tools\$kitVersion\x64\infverif.exe"
$output = Join-Path $PSScriptRoot 'bin\lab-x64'
$package = Join-Path $output 'driver'
New-Item -ItemType Directory -Force -Path $package | Out-Null

function Invoke-Tool([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable exited with code $LASTEXITCODE" }
}

$driver = Join-Path $package 'LegacyIpadStatus.sys'
$buildArgs = @(
    (Join-Path $PSScriptRoot 'LegacyIpadStatus.vcxproj'), '/nologo', '/v:minimal',
    '/p:Configuration=Debug', '/p:Platform=x64', "/p:WdkPackages=$Tools"
)
if ($LabWithoutSpectreLibraries) { $buildArgs += '/p:SpectreMitigation=false' }
Invoke-Tool $msbuild $buildArgs
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\wdk-x64\LegacyIpadStatus.sys') -Destination $driver

$probe = Join-Path $output 'IpadStatusProbe.exe'
Invoke-Tool $cl @(
    '/nologo', '/W4', '/WX', '/O2', '/MT', '/DUNICODE', '/D_UNICODE',
    "/I$vc\include", "/I$sdk\Include\$kitVersion\shared", "/I$sdk\Include\$kitVersion\um",
    "/I$sdk\Include\$kitVersion\ucrt", "/Fo$output\probe.obj", "/Fe$probe",
    (Join-Path $PSScriptRoot 'probe.c'), '/link', "/LIBPATH:$vc\lib\x64",
    "/LIBPATH:$sdkX64\um\x64", "/LIBPATH:$sdkX64\ucrt\x64", 'cfgmgr32.lib'
)

$inf = Join-Path $package 'LegacyIpadStatus.inf'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyIpadStatus.inf') -Destination $inf
Invoke-Tool $infverif @('/w', $inf)

# Keep the lab signer out of the host certificate stores and ship only its public certificate.
$key = [Security.Cryptography.RSA]::Create(2048)
$cert = $null
$pfx = Join-Path ([IO.Path]::GetTempPath()) ('legacy-ipad-' + [guid]::NewGuid() + '.pfx')
$password = [guid]::NewGuid().ToString('N')
try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=Legacy iPad GET_STATUS Lab', $key, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new('DigitalSignature', $true))
    $usage = [Security.Cryptography.OidCollection]::new()
    [void]$usage.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usage, $true))
    $now = [DateTimeOffset]::UtcNow
    $cert = $request.CreateSelfSigned($now.AddHours(-1), $now.AddDays(30))
    [IO.File]::WriteAllBytes($pfx, $cert.Export('Pfx', $password))
    [IO.File]::WriteAllBytes((Join-Path $output 'LabSigner.cer'), $cert.Export('Cert'))
    Invoke-Tool $sign @('sign', '/fd', 'SHA256', '/f', $pfx, '/p', $password, $driver)
    Invoke-Tool $catalog @("/driver:$package", '/os:10_GE_X64', '/verbose')
    Invoke-Tool $sign @('sign', '/fd', 'SHA256', '/f', $pfx, '/p', $password, (Join-Path $package 'LegacyIpadStatus.cat'))
} finally {
    if ($cert) { $cert.Dispose() }
    $key.Dispose()
    if (Test-Path -LiteralPath $pfx) { Remove-Item -LiteralPath $pfx }
}
