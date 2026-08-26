#Requires -Version 5.1
<#
.SYNOPSIS
    Installation du paquet MSIX PROFstudio (sideload) :
    1. Débloque les fichiers téléchargés (suppression du Mark of the Web).
    2. Installe le certificat dans Autorités racines de confiance et Personnes de confiance.
    3. Enregistre et installe le paquet MSIX.
#>
[CmdletBinding()]
param (
    [string]$MsixPath = "",
    [string]$CertPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($MsixPath)) {
    $msixCandidate = Get-ChildItem -Path $PSScriptRoot -Filter "*.msix" | Select-Object -First 1
    $MsixPath = if ($msixCandidate) { $msixCandidate.FullName } else { Join-Path $PSScriptRoot "PROFstudio-v1.2.0-x64.msix" }
}

if ([string]::IsNullOrWhiteSpace($CertPath)) {
    $certCandidate = Get-ChildItem -Path $PSScriptRoot -Filter "*.cer" | Select-Object -First 1
    $CertPath = if ($certCandidate) { $certCandidate.FullName } else { Join-Path $PSScriptRoot "PROFstudio-DevCert.cer" }
}

# ── Déblocage préalable des fichiers (Mark of the Web) ──────────────────────
try {
    Get-ChildItem -Path $PSScriptRoot -Recurse | Unblock-File -ErrorAction SilentlyContinue
} catch { }

# ── Réélève le script avec privilèges administrateur si nécessaire ──────────
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Demande d'élévation des privilèges Administrateur (UAC)..." -ForegroundColor Yellow
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"", "-MsixPath", "`"$MsixPath`"", "-CertPath", "`"$CertPath`"")
    Start-Process powershell -ArgumentList $argList -Verb RunAs
    exit
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "         Installation & Approbation MSIX — PROFstudio        " -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

foreach ($required in @($MsixPath, $CertPath)) {
    if (-not (Test-Path $required)) {
        Write-Host "  [ERREUR] Fichier introuvable : $required" -ForegroundColor Red
        exit 1
    }
}

# ── 1. Installation du certificat dans Root et TrustedPeople ────────────────
try {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertPath)
    
    # TrustedPeople (Personnes de confiance)
    $storePeople = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "LocalMachine")
    try {
        $storePeople.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $storePeople.Add($cert)
    } finally {
        $storePeople.Close()
    }

    # Root (Autorités de certification racines de confiance pour contourner l'erreur de confiance)
    $storeRoot = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
    try {
        $storeRoot.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $storeRoot.Add($cert)
    } finally {
        $storeRoot.Close()
    }

    Write-Host "  [OK] Certificat installe dans les Autorites de confiance ($($cert.Subject))." -ForegroundColor Green
}
catch {
    Write-Host "  [AVERTISSEMENT] Erreur lors de l'enregistrement du certificat : $($_.Exception.Message)" -ForegroundColor Yellow
}

# ── 2. Enregistrement et installation du paquet MSIX ────────────────────────
try {
    Write-Host "  Installation du paquet $MsixPath ..." -ForegroundColor Gray
    Add-AppxPackage -Path $MsixPath -ErrorAction Stop
    Write-Host "  [OK] PROFstudio est installe avec succes !" -ForegroundColor Green
    Write-Host "  Vous pouvez desormais le lancer depuis le menu Demarrer." -ForegroundColor White
}
catch {
    Write-Host "  [ERREUR] Echec de l'installation du paquet MSIX :" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Astuce : Vous pouvez aussi utiliser l'installeur standard PROFstudio-Setup-v1.2.0-x64.exe" -ForegroundColor Cyan
    exit 1
}

Write-Host ""
