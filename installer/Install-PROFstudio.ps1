<#
.SYNOPSIS
    Installation per-utilisateur de PROFstudio (sans droits administrateur).
.DESCRIPTION
    Copie l'application dans %LocalAppData%\Programs\PROFstudio, verifie la
    presence du runtime WebView2, cree les raccourcis (menu Demarrer + bureau)
    et genere un desinstalleur local.
#>
[CmdletBinding()]
param (
    [string]$Destination = "$env:LOCALAPPDATA\Programs\PROFstudio"
)

$ErrorActionPreference = "Stop"

$sourceDir = Join-Path $PSScriptRoot "app"
$exeName = "FicheGen.App.exe"

function Write-Info([string]$msg) { Write-Host "  $msg" -ForegroundColor Gray }
function Write-Ok([string]$msg) { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Fail([string]$msg) { Write-Host "  [ERREUR] $msg" -ForegroundColor Red }

Write-Host ""
Write-Host "=== Installation de PROFstudio ===" -ForegroundColor Cyan
Write-Host ""

# ── Prerequis 1 : Windows 10 1809 (build 17763) ou plus recent ──────────────
$osVersion = [System.Environment]::OSVersion.Version
if ($osVersion.Build -lt 17763) {
    Write-Fail "Windows 10 1809 (build 17763) ou superieur est requis (detecte : build $($osVersion.Build))."
    exit 1
}
Write-Ok "Windows detecte (build $($osVersion.Build))."

# ── Prerequis 2 : runtime WebView2 ───────────────────────────────────────────
$webView2Keys = @(
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
    "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
    "HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
)
$hasWebView2 = $false
foreach ($key in $webView2Keys) {
    if (Test-Path $key) { $hasWebView2 = $true; break }
}
if (-not $hasWebView2) {
    Write-Host ""
    Write-Host "  [ATTENTION] Le runtime Microsoft WebView2 semble absent." -ForegroundColor Yellow
    Write-Host "  PROFstudio en a besoin pour afficher les apercus de documents." -ForegroundColor Yellow
    $answer = Read-Host "  Ouvrir la page de telechargement du runtime WebView2 maintenant ? (O/n)"
    if ($answer -notin @("n", "N", "non")) {
        Start-Process "https://go.microsoft.com/fwlink/?linkid=2124701"
        Write-Info "Installez le runtime, puis relancez ce programme d'installation si besoin."
    }
}
else {
    Write-Ok "Runtime WebView2 detecte."
}

# ── Ferme une instance en cours d'execution ─────────────────────────────────
$running = Get-Process -Name $exeName -ErrorAction SilentlyContinue
if ($running) {
    Write-Info "Fermeture de l'instance en cours d'execution..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# ── Copie des fichiers ───────────────────────────────────────────────────────
if (-not (Test-Path $sourceDir)) {
    Write-Fail "Dossier source introuvable : $sourceDir"
    exit 1
}

Write-Info "Copie des fichiers vers $Destination ..."
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
robocopy $sourceDir $Destination /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
$robocopyCode = $LASTEXITCODE
if ($robocopyCode -ge 8) {
    Write-Fail "La copie des fichiers a echoue (robocopy code $robocopyCode)."
    exit 1
}
try {
    Get-ChildItem $Destination -Recurse | Unblock-File -ErrorAction SilentlyContinue
} catch { }
Write-Ok "Fichiers installes et debloques."

# ── Raccourcis ───────────────────────────────────────────────────────────────
$wsh = New-Object -ComObject WScript.Shell
$exePath = Join-Path $Destination $exeName

$startMenuLnk = Join-Path $wsh.SpecialFolders.Item("Programs") "PROFstudio.lnk"
$sc = $wsh.CreateShortcut($startMenuLnk)
$sc.TargetPath = $exePath
$sc.WorkingDirectory = $Destination
$sc.Description = "PROFstudio - Generateur IA de fiches pedagogiques"
$sc.Save()
Write-Ok "Raccourci menu Demarrer cree."

$desktopLnk = Join-Path $wsh.SpecialFolders.Item("Desktop") "PROFstudio.lnk"
$sc = $wsh.CreateShortcut($desktopLnk)
$sc.TargetPath = $exePath
$sc.WorkingDirectory = $Destination
$sc.Description = "PROFstudio - Generateur IA de fiches pedagogiques"
$sc.Save()
Write-Ok "Raccourci bureau cree."

# ── Desinstalleur local ──────────────────────────────────────────────────────
$uninstallCmd = @"
@echo off
echo Desinstallation de PROFstudio...
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\PROFstudio.lnk" 2>nul
del "%USERPROFILE%\Desktop\PROFstudio.lnk" 2>nul
rd /s /q "%LOCALAPPDATA%\Programs\PROFstudio"
echo PROFstudio a ete desinstalle.
pause
"@
Set-Content -Path (Join-Path $Destination "Uninstall-PROFstudio.cmd") -Value $uninstallCmd -Encoding Ascii

Write-Host ""
Write-Host "=== PROFstudio est installe ! ===" -ForegroundColor Green
Write-Host ""
Write-Host "  Emplacement : $Destination"
Write-Host "  Lancer      : menu Demarrer > PROFstudio (ou le raccourci bureau)"
Write-Host "  Desinstaller: $Destination\Uninstall-PROFstudio.cmd"
Write-Host ""
