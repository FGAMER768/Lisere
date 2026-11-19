[CmdletBinding()]
param(
    [string]$ArchivePath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root "ffmpeg\manifest.json"
$outputDirectory = Join-Path $root "tools\ffmpeg"

if (-not (Test-Path $manifestPath)) {
    throw "ffmpeg/manifest.json est absent."
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$archive = if ($ArchivePath) {
    (Resolve-Path $ArchivePath).Path
} else {
    Join-Path $root "artifacts\ffmpeg-download\ffmpeg.zip"
}

if (-not (Test-Path $archive)) {
    $downloadDirectory = Join-Path $root "artifacts\ffmpeg-download"
    New-Item -ItemType Directory -Force -Path $downloadDirectory | Out-Null
    $archive = Join-Path $downloadDirectory "ffmpeg.zip"
    Write-Host "Téléchargement de FFmpeg $($manifest.version)..."
    Invoke-WebRequest -Uri $manifest.archiveUrl -OutFile $archive
}

$archiveHash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($archiveHash -ne $manifest.archiveSha256.ToLowerInvariant()) {
    throw "L'archive FFmpeg ne correspond pas à l'empreinte du manifeste."
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)

try {
    foreach ($file in $manifest.files.PSObject.Properties) {
        $name = $file.Name
        if ($name -ne [System.IO.Path]::GetFileName($name)) {
            throw "Nom de fichier FFmpeg invalide dans le manifeste : $name"
        }

        $entryName = "$($manifest.archivePrefix)/bin/$name"
        $entry = $zip.GetEntry($entryName)
        if ($null -eq $entry) {
            throw "Entrée absente de l'archive FFmpeg : $entryName"
        }

        $destination = Join-Path $outputDirectory $name
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)

        $actualHash = (Get-FileHash $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $file.Value.ToLowerInvariant()) {
            throw "Le fichier FFmpeg extrait ne correspond pas au manifeste : $name"
        }
    }
}
finally {
    $zip.Dispose()
}

Write-Host "FFmpeg $($manifest.version) est prêt dans tools/ffmpeg."
Write-Host "Source : $($manifest.source)"
