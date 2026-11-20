[CmdletBinding(DefaultParameterSetName = "Thumbprint")]
param(
    [Parameter(Mandatory, ParameterSetName = "Thumbprint")]
    [string]$Thumbprint,

    [Parameter(Mandatory, ParameterSetName = "Pfx")]
    [string]$CertificatePath,

    [Parameter(ParameterSetName = "Pfx")]
    [SecureString]$CertificatePassword,

    [string[]]$Files,

    [string]$TimestampServer = "http://timestamp.digicert.com",

    [switch]$AllowUntrusted
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not $PSBoundParameters.ContainsKey("Files")) {
    $defaultFiles = @(
        (Join-Path $root "artifacts\installer\Lisere-Setup.exe"),
        (Join-Path $root "artifacts\publish\Lisere.exe")
    )
    $Files = @($defaultFiles | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($Files.Count -eq 0) {
        throw "Aucun artefact à signer. Construisez d'abord l'application ou l'installateur."
    }
}

$resolvedFiles = @()
foreach ($file in $Files) {
    $path = if ([System.IO.Path]::IsPathRooted($file)) {
        $file
    } else {
        Join-Path $root $file
    }

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Fichier introuvable : $path"
    }

    $resolvedFiles += (Get-Item -LiteralPath $path)
}

$certificate = $null
try {
    if ($PSCmdlet.ParameterSetName -eq "Thumbprint") {
        $normalized = $Thumbprint -replace "\s", ""
        $candidates = @(
            Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
                Where-Object { $_.Thumbprint -eq $normalized }
        )
        if ($candidates.Count -eq 0) {
            throw "Aucun certificat avec l'empreinte $normalized dans CurrentUser\My ou LocalMachine\My."
        }

        $certificate = $candidates[0]
    } else {
        $path = (Resolve-Path -LiteralPath $CertificatePath).Path
        $certificate = if ($CertificatePassword) {
            [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
                $path,
                $CertificatePassword)
        } else {
            [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($path)
        }
    }

    if (-not $certificate.HasPrivateKey) {
        throw "Le certificat ne contient pas de clé privée : impossible de signer."
    }

    $usage = $certificate.Extensions |
        Where-Object { $_.Oid.Value -eq "2.5.29.37" } |
        Select-Object -First 1
    if ($usage -and -not ($usage.EnhancedKeyUsages | Where-Object { $_.Value -eq "1.3.6.1.5.5.7.3.3" })) {
        throw "Le certificat n'autorise pas la signature de code (EKU 1.3.6.1.5.5.7.3.3 absent)."
    }

    Write-Host "Certificat : $($certificate.Subject)"
    Write-Host "Empreinte  : $($certificate.Thumbprint)"
    Write-Host ""

    foreach ($file in $resolvedFiles) {
        $signatureParameters = @{
            FilePath = $file.FullName
            Certificate = $certificate
            HashAlgorithm = "SHA256"
        }
        if ($TimestampServer) {
            $signatureParameters.TimestampServer = $TimestampServer
        }

        Set-AuthenticodeSignature @signatureParameters | Out-Null
        $status = (Get-AuthenticodeSignature -LiteralPath $file.FullName).Status

        if ($status -ne "Valid" -and -not $AllowUntrusted) {
            throw "Signature de $($file.Name) refusée : statut $status. Vérifiez que le certificat est approuvé ou utilisez -AllowUntrusted pour un test local."
        }

        $note = if ($status -eq "Valid") { "valide" } else { "non approuvée localement ($status)" }
        Write-Host "Signé : $($file.FullName) [$note]"
    }
} finally {
    if ($certificate) {
        $certificate.Dispose()
    }
}
