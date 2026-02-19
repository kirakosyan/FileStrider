<#
.SYNOPSIS
    Packages FileStrider as an MSIX for distribution.

.DESCRIPTION
    This script publishes the app as a self-contained win-x64 binary,
    then uses MakeAppx.exe and SignTool.exe from the Windows SDK to
    create and optionally sign the MSIX package.

.PARAMETER CertificatePath
    Path to a .pfx code-signing certificate.  When omitted the MSIX is
    created unsigned (suitable for sideloading during development).

.PARAMETER CertificatePassword
    Password for the .pfx certificate.

.EXAMPLE
    .\Package-Msix.ps1
    .\Package-Msix.ps1 -CertificatePath .\cert.pfx -CertificatePassword secret
#>
param(
    [string]$CertificatePath,
    [string]$CertificatePassword
)

$ErrorActionPreference = 'Stop'

$projectDir  = Split-Path -Parent $PSScriptRoot
$appProject  = Join-Path $projectDir 'src\FileStrider.MauiApp\FileStrider.MauiApp.csproj'
$publishDir  = Join-Path $projectDir 'src\FileStrider.MauiApp\bin\publish\win-x64'
$manifestSrc = Join-Path $projectDir 'src\FileStrider.MauiApp\Package.appxmanifest'
$msixOutput  = Join-Path $projectDir 'FileStrider.msix'
$assetsDir   = Join-Path $projectDir 'src\FileStrider.MauiApp\Assets\MsixLogo'

# Step 1 – Publish
Write-Host '=== Publishing FileStrider (win-x64, self-contained) ===' -ForegroundColor Cyan
dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# Step 2 – Copy manifest & assets into publish folder
Write-Host '=== Copying manifest & MSIX assets ===' -ForegroundColor Cyan
Copy-Item $manifestSrc -Destination (Join-Path $publishDir 'AppxManifest.xml') -Force

$targetAssets = Join-Path $publishDir 'Assets\MsixLogo'
New-Item -ItemType Directory -Force -Path $targetAssets | Out-Null
Copy-Item "$assetsDir\*" -Destination $targetAssets -Force

# Step 3 – Locate MakeAppx.exe
$sdkBinRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$makeAppx = Get-ChildItem -Path $sdkBinRoot -Recurse -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'x64' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $makeAppx) {
    Write-Warning 'MakeAppx.exe not found. Install the Windows 10/11 SDK.'
    Write-Host "You can create the MSIX manually:`n  makeappx pack /d `"$publishDir`" /p `"$msixOutput`"" -ForegroundColor Yellow
    exit 1
}

# Step 4 – Create MSIX
Write-Host "=== Creating MSIX package ===" -ForegroundColor Cyan
& $makeAppx pack /d $publishDir /p $msixOutput /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx failed' }

# Step 5 – Sign (optional)
if ($CertificatePath) {
    $signTool = Get-ChildItem -Path $sdkBinRoot -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'x64' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName

    if (-not $signTool) {
        Write-Warning 'SignTool.exe not found. The MSIX was created but is unsigned.'
    } else {
        Write-Host '=== Signing MSIX ===' -ForegroundColor Cyan
        $signArgs = @('sign', '/fd', 'SHA256', '/f', $CertificatePath)
        if ($CertificatePassword) { $signArgs += @('/p', $CertificatePassword) }
        $signArgs += $msixOutput
        & $signTool @signArgs
        if ($LASTEXITCODE -ne 0) { throw 'SignTool failed' }
    }
}

Write-Host "`n=== Done! MSIX created at: $msixOutput ===" -ForegroundColor Green
