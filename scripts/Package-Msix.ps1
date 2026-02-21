<#
.SYNOPSIS
    Packages FileStrider as MSIX packages for distribution.

.DESCRIPTION
    This script publishes the app as self-contained binaries for one or
    more architectures (x64 and arm64 by default), then uses MakeAppx.exe
    and optionally SignTool.exe from the Windows SDK to create and sign
    the MSIX packages.

    Output files are named:  FileStrider_<Version>_<arch>.msix

.PARAMETER Architecture
    One or more architectures to build.  Defaults to @('x64','arm64').
    Valid values: x64, arm64.

.PARAMETER CertificatePath
    Path to a .pfx code-signing certificate.  When omitted the MSIX is
    created unsigned (suitable for sideloading during development).

.PARAMETER CertificatePassword
    Password for the .pfx certificate.

.EXAMPLE
    .\Package-Msix.ps1
    .\Package-Msix.ps1 -Architecture x64
    .\Package-Msix.ps1 -Architecture x64,arm64 -CertificatePath .\cert.pfx -CertificatePassword secret
#>
param(
    [ValidateSet('x64','arm64')]
    [string[]]$Architecture = @('x64','arm64'),
    [string]$CertificatePath,
    [string]$CertificatePassword
)

$ErrorActionPreference = 'Stop'

$projectDir  = Split-Path -Parent $PSScriptRoot
$appProject  = Join-Path $projectDir 'src\FileStrider.MauiApp\FileStrider.MauiApp.csproj'
$manifestSrc = Join-Path $projectDir 'src\FileStrider.MauiApp\Package.appxmanifest'
$assetsDir   = Join-Path $projectDir 'src\FileStrider.MauiApp\Assets\MsixLogo'

# Read version from manifest
[xml]$manifestXml = Get-Content $manifestSrc
$version = $manifestXml.Package.Identity.Version
Write-Host "Package version: $version" -ForegroundColor Cyan

# Locate MakeAppx.exe
$sdkBinRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$makeAppx = Get-ChildItem -Path $sdkBinRoot -Recurse -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'x64' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $makeAppx) {
    Write-Warning 'MakeAppx.exe not found. Install the Windows 10/11 SDK.'
    exit 1
}

# Locate SignTool.exe (if certificate provided)
$signTool = $null
if ($CertificatePath) {
    $signTool = Get-ChildItem -Path $sdkBinRoot -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'x64' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName

    if (-not $signTool) {
        Write-Warning 'SignTool.exe not found. Packages will be created unsigned.'
    }
}

# Map architecture names
$archMap = @{
    'x64'   = @{ RID = 'win-x64';   ManifestArch = 'x64'   }
    'arm64' = @{ RID = 'win-arm64';  ManifestArch = 'arm64' }
}

$createdPackages = @()

foreach ($arch in $Architecture) {
    $rid          = $archMap[$arch].RID
    $manifestArch = $archMap[$arch].ManifestArch
    $publishDir   = Join-Path $projectDir "src\FileStrider.MauiApp\bin\publish\$rid"
    $msixOutput   = Join-Path $projectDir "FileStrider_${version}_${arch}.msix"

    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "=== Building for $arch ($rid) ===" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    # Step 1 – Publish
    Write-Host "=== Publishing FileStrider ($rid, self-contained) ===" -ForegroundColor Cyan
    dotnet publish $appProject `
        -c Release `
        -r $rid `
        --self-contained true `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

    # Step 2 – Copy manifest & assets into publish folder
    #          Patch ProcessorArchitecture to match the target arch
    Write-Host '=== Copying manifest & MSIX assets ===' -ForegroundColor Cyan
    [xml]$archManifest = Get-Content $manifestSrc
    $archManifest.Package.Identity.ProcessorArchitecture = $manifestArch
    $archManifest.Save((Join-Path $publishDir 'AppxManifest.xml'))

    $targetAssets = Join-Path $publishDir 'Assets\MsixLogo'
    New-Item -ItemType Directory -Force -Path $targetAssets | Out-Null
    Copy-Item "$assetsDir\*" -Destination $targetAssets -Force

    # Step 3 – Create MSIX
    Write-Host "=== Creating MSIX package: FileStrider_${version}_${arch}.msix ===" -ForegroundColor Cyan
    & $makeAppx pack /d $publishDir /p $msixOutput /o
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed for $arch" }

    # Step 4 – Sign (optional)
    if ($CertificatePath -and $signTool) {
        Write-Host '=== Signing MSIX ===' -ForegroundColor Cyan
        $signArgs = @('sign', '/fd', 'SHA256', '/f', $CertificatePath)
        if ($CertificatePassword) { $signArgs += @('/p', $CertificatePassword) }
        $signArgs += $msixOutput
        & $signTool @signArgs
        if ($LASTEXITCODE -ne 0) { throw "SignTool failed for $arch" }
    }

    $createdPackages += $msixOutput
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "=== Done! Packages created: ===" -ForegroundColor Green
foreach ($pkg in $createdPackages) {
    Write-Host "  $pkg" -ForegroundColor Green
}
Write-Host "========================================" -ForegroundColor Green
