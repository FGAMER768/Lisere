[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipPayloadCheck,
    [string]$CertificateThumbprint,
    [string]$CertificatePath,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Lisere.Setup\Lisere.Setup.csproj"
$payload = Join-Path $root "Update\Lisere.zip"
$output = Join-Path $root "artifacts\installer"
$rawOutput = Join-Path $root "artifacts\setup-build"
$versionFile = Join-Path $root "src\Lisere\Lisere.csproj"

if (-not $SkipPayloadCheck -and -not (Test-Path $payload)) {
    throw "Le paquet applicatif est absent. Lancez d'abord scripts/publish-update.ps1."
}

$projectXml = [xml](Get-Content $versionFile)
$version = @($projectXml.Project.PropertyGroup.Version) |
    Where-Object { $_ } |
    Select-Object -First 1
if (-not $version) {
    throw "Aucune <Version> n'est définie dans src/Lisere/Lisere.csproj."
}

if (-not $SkipPayloadCheck) {
    $publishedVersion = (Get-Content (Join-Path $root "Update\version.txt") -Raw).Trim()
    if ($publishedVersion -ne $version) {
        throw "Le paquet Update\Lisere.zip est en version $publishedVersion, pas $version. Relancez scripts/publish-update.ps1."
    }
}

$parts = $version.Split(".")
while ($parts.Count -lt 4) {
    $parts += "0"
}
$assemblyVersion = ($parts[0..3] -join ".")

if (Test-Path $rawOutput) {
    Remove-Item $rawOutput -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet build $project `
    -c $Configuration `
    -p:Version=$version `
    -p:FileVersion=$assemblyVersion `
    -p:AssemblyVersion=$assemblyVersion `
    -o $rawOutput
if ($LASTEXITCODE -ne 0) {
    throw "La compilation du programme d'installation a échoué."
}

$built = Join-Path $rawOutput "LisereSetup.exe"
if (-not (Test-Path $built)) {
    throw "Le programme d'installation n'a pas été produit."
}

$setup = Join-Path $output "Lisere-Setup-$version.exe"
$stable = Join-Path $output "Lisere-Setup.exe"
Copy-Item $built $setup -Force
Copy-Item $built $stable -Force

if ($CertificateThumbprint -and $CertificatePath) {
    throw "Choisissez -CertificateThumbprint ou -CertificatePath, pas les deux."
}

if ($CertificateThumbprint -or $CertificatePath) {
    $signArguments = @{
        Files = @($setup, $stable)
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
        throw "Signature de l'installateur impossible : $_"
    }
}

$hash = (Get-FileHash $stable -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path (Join-Path $output "Lisere-Setup.exe.sha256") `
    -Value "$hash  Lisere-Setup.exe" `
    -NoNewline `
    -Encoding ascii

$sizeMegabytes = [math]::Round((Get-Item $stable).Length / 1MB, 1)
Write-Host ""
Write-Host "Programme d'installation prêt."
Write-Host "Fichier : artifacts\installer\Lisere-Setup.exe"
Write-Host "Version : $version"
Write-Host "Taille : $sizeMegabytes Mo"
Write-Host "Empreinte : $hash"
