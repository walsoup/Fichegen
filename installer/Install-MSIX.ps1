#Requires -Version 5.1
<#
.SYNOPSIS
    Installation du paquet MSIX PROFstudio (sideload) : importe le certificat
    de developpement puis enregistre le paquet.
#>
[CmdletBinding()]
param (
    [string]$MsixPath = (Join-Path $PSScriptRoot "PROFstudio-v1.0.0-x64.msix"),
    [string]$CertPath = (Join-Path $PSScriptRoot "PROFstudio-DevCert.cer")
)

$ErrorActionPreference = "Stop"

# ── Reeleve le script avec privileges administrateur (requis pour le magasin de certificats) ──
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"", "-MsixPath", "`"$MsixPath`"", "-CertPath", "`"$CertPath`"")
    Start-Process powershell -ArgumentList $argList -Verb RunAs
    exit
}

Write-Host ""
Write-Host "=== Installation MSIX de PROFstudio ===" -ForegroundColor Cyan
Write-Host ""

foreach ($required in @($MsixPath, $CertPath)) {
    if (-not (Test-Path $required)) {
        Write-Host "  [ERREUR] Fichier introuvable : $required" -ForegroundColor Red
        exit 1
    }
}

# ── 1. Certificat de signature ───────────────────────────────────────────────
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertPath)
$storeName = [System.Security.Cryptography.X509Certificates.StoreName]::TrustedPeople
$existing = Get-ChildItem "Cert:\LocalMachine\TrustedPeople" | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
if (-not $existing) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, "LocalMachine")
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $store.Add($cert)
    } finally {
        $store.Close()
    }
    Write-Host "  [OK] Certificat importe dans TrustedPeople ($($cert.Subject))." -ForegroundColor Green
}
else {
    Write-Host "  [OK] Certificat deja present dans TrustedPeople." -ForegroundColor Green
}

# ── 2. Enregistrement du paquet ──────────────────────────────────────────────
try {
    Add-AppxPackage -Path $MsixPath -ErrorAction Stop
    Write-Host "  [OK] PROFstudio est installe. Lancez-le depuis le menu Demarrer." -ForegroundColor Green
}
catch {
    Write-Host "  [ERREUR] Echec de l'installation du paquet :" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
