[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$CertificateThumbprint,
    [string]$CertificatePath,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Lisere\Lisere.csproj"
$publishDirectory = Join-Path $root "artifacts\publish"
$updateDirectory = Join-Path $root "Update"
$package = Join-Path $updateDirectory "Lisere.zip"
$checksum = Join-Path $updateDirectory "Lisere.zip.sha256"

if (-not (Test-Path (Join-Path $root "tools\ffmpeg\ffmpeg.exe"))) {
    & (Join-Path $PSScriptRoot "fetch-ffmpeg.ps1")
}

if (Test-Path $publishDirectory) {
    Remove-Item $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $updateDirectory | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish a échoué."
}

$projectXml = [xml](Get-Content $project)
$version = @($projectXml.Project.PropertyGroup.Version) |
    Where-Object { $_ } |
    Select-Object -First 1
if (-not $version) {
    throw "Aucune <Version> n'est définie dans src/Lisere/Lisere.csproj."
}

if (-not (Test-Path (Join-Path $publishDirectory "Lisere.exe"))) {
    throw "La publication ne contient pas Lisere.exe."
}

if ($CertificateThumbprint -and $CertificatePath) {
    throw "Choisissez -CertificateThumbprint ou -CertificatePath, pas les deux."
}

if ($CertificateThumbprint -or $CertificatePath) {
    $signArguments = @{
        Files = @((Join-Path $publishDirectory "Lisere.exe"))
        TimestampServer = $TimestampServer
    }
    if ($CertificateThumbprint) {
        $signArguments.Thumbprint = $CertificateThumbprint
    } else {
        $signArguments.CertificatePath = $CertificatePath
    }

    try {
        & (Join-Path $PSScriptRoot "sign-artifacts.ps1") @signArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Le script de signature a échoué."
        }
    } catch {
        throw "Signature de Lisere.exe impossible : $_"
    }
}

foreach ($required in @("ffmpeg\ffmpeg.exe", "ffmpeg\ffprobe.exe", "ffmpeg\manifest.json")) {
    if (-not (Test-Path (Join-Path $publishDirectory $required))) {
        throw "La publication ne contient pas le paquet FFmpeg intégré ($required)."
    }
}

Remove-Item $package -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $package -CompressionLevel Optimal

$hash = (Get-FileHash $package -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path (Join-Path $updateDirectory "version.txt") -Value $version -NoNewline -Encoding ascii
Set-Content -Path $checksum -Value "$hash  Lisere.zip" -NoNewline -Encoding ascii

Write-Host ""
Write-Host "Version $version prête."
Write-Host "Paquet : Update\Lisere.zip"
Write-Host "Empreinte : $hash"
Write-Host ""
Write-Host "Lancez ensuite scripts\build-setup.ps1 pour produire Lisere-Setup.exe."
Write-Host "Créez une release GitHub dont le tag correspond à la version, puis joignez :"
Write-Host "  - artifacts\installer\Lisere-Setup.exe"
Write-Host "  - artifacts\installer\Lisere-Setup.exe.sha256"
Write-Host "  - Update\Lisere.zip"
Write-Host "  - Update\Lisere.zip.sha256"
Write-Host "Committez Update\version.txt une fois les assets téléversés."
