<#
.SYNOPSIS
    Script de génération et d'empaquetage complet des installeurs PROFstudio pour Windows.
.DESCRIPTION
    Construit la version Release x64, puis génère :
    1. L'installeur autonome Windows (Setup.cmd + Install-PROFstudio.ps1)
    2. Le paquet MSIX signé (avec certificat de dev et script d'installation)
    3. L'archive portable autonome (.zip)
    4. Le fichier manifeste auto-update (.appinstaller)
    5. L'exécutable Inno Setup (si le compilateur ISCC est détecté)
#>

[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "1.2.0.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$appProj = "$repoRoot\src\FicheGen.App\FicheGen.App.csproj"
$publishDir = "$repoRoot\artifacts\publish\$RuntimeIdentifier"
$distDir = "$repoRoot\artifacts\dist"
$assetsDir = "$repoRoot\src\FicheGen.App\Assets"
$shortVersion = "1.2.0"

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host ">>> $msg" -ForegroundColor Cyan
}

function Write-Success([string]$msg) {
    Write-Host "  [OK] $msg" -ForegroundColor Green
}

# ==============================================================================
# Étape 1 : Compilation et Publication .NET
# ==============================================================================
Write-Step "1/5. Publication du projet FicheGen.App ($Configuration - $RuntimeIdentifier)..."

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

$publishArgs = @(
    "publish", $appProj,
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "-p:Platform=$Platform",
    "--self-contained", "true",
    "-p:WindowsPackageType=None",
    "-p:PublishReadyToRun=true",
    "-o", $publishDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "Échec de la publication dotnet."
}
Write-Success "Publication terminée dans $publishDir"

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

# ==============================================================================
# Étape 2 : Création du paquet Standalone Setup (Dossier d'installation directe)
# ==============================================================================
Write-Step "2/5. Création du bundle Standalone Setup..."
$standaloneDir = "$distDir\PROFstudio-Setup-win-$Platform"
if (Test-Path $standaloneDir) {
    Remove-Item -Path $standaloneDir -Recurse -Force
}
New-Item -ItemType Directory -Path $standaloneDir -Force | Out-Null

# Copy publish files to app subdirectory
$appSubDir = "$standaloneDir\app"
New-Item -ItemType Directory -Path $appSubDir -Force | Out-Null
Copy-Item -Path "$publishDir\*" -Destination $appSubDir -Recurse -Force

# Copy installer scripts to root of bundle
Copy-Item -Path "$repoRoot\installer\Setup.cmd" -Destination "$standaloneDir\Setup.cmd" -Force
Copy-Item -Path "$repoRoot\installer\Install-PROFstudio.ps1" -Destination "$standaloneDir\Install-PROFstudio.ps1" -Force

Write-Success "Bundle d'installation autonome créé : $standaloneDir"

# ==============================================================================
# Étape 3 : Création de l'archive Portable .ZIP
# ==============================================================================
Write-Step "3/5. Compression de l'archive portable .ZIP..."
$zipPath = "$distDir\PROFstudio-v$shortVersion-$RuntimeIdentifier-portable.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
Write-Success "Archive portable générée : $zipPath"

# ==============================================================================
# Étape 4 : Création et Signature du paquet MSIX
# ==============================================================================
Write-Step "4/5. Création et signature du paquet MSIX..."

$stageDir = "$repoRoot\artifacts\msix-stage"
if (Test-Path $stageDir) {
    Remove-Item -Path $stageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

Copy-Item -Path "$publishDir\*" -Destination $stageDir -Recurse -Force
Copy-Item -Path "$repoRoot\src\FicheGen.App\Package.appxmanifest" -Destination "$stageDir\AppxManifest.xml" -Force
if (-not (Test-Path "$stageDir\Assets")) {
    New-Item -ItemType Directory -Path "$stageDir\Assets" -Force | Out-Null
}
Copy-Item -Path "$assetsDir\*" -Destination "$stageDir\Assets\" -Force

# Find MakeAppx and SignTool
$sdkBin = "C:\Program Files (x86)\Windows Kits\10\bin"
$makeAppx = (Get-ChildItem -Path $sdkBin -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*x64*" } | Select-Object -First 1).FullName
$signtool = (Get-ChildItem -Path $sdkBin -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*x64*" } | Select-Object -First 1).FullName

$msixFile = "$distDir\PROFstudio-v$shortVersion-$Platform.msix"
$certFile = "$distDir\PROFstudio-DevCert.cer"

if ($makeAppx -and (Test-Path $makeAppx)) {
    & $makeAppx pack /d $stageDir /p $msixFile /o
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Paquet MSIX empaqueté avec MakeAppx : $msixFile"
        
        # Self-signed certificate for local testing
        $certSubject = "CN=FicheGen"
        $existingCert = Get-ChildItem -Path "Cert:\CurrentUser\My" | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
        if (-not $existingCert) {
            $existingCert = New-SelfSignedCertificate -Type Custom -Subject $certSubject -KeyUsage DigitalSignature -FriendlyName "PROFstudio Developer Certificate" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
        }
        
        Export-Certificate -Cert $existingCert -FilePath $certFile -Type CERT -Force | Out-Null
        
        if ($signtool -and (Test-Path $signtool)) {
            & $signtool sign /fd SHA256 /sha1 $existingCert.Thumbprint $msixFile
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Paquet MSIX signé avec succès (Thumbprint: $($existingCert.Thumbprint))"
            }
        }
        
        # Copy MSIX installer helpers
        Copy-Item -Path "$repoRoot\installer\Install-MSIX.ps1" -Destination "$distDir\Install-MSIX.ps1" -Force
        Copy-Item -Path "$repoRoot\installer\Install-MSIX.cmd" -Destination "$distDir\Install-MSIX.cmd" -Force
    }
} else {
    Write-Warning "MakeAppx.exe introuvable dans le Windows SDK. Étape MSIX ignorée."
}

# Clean msix staging folder
if (Test-Path $stageDir) {
    Remove-Item -Path $stageDir -Recurse -Force
}

# ==============================================================================
# Étape 5 : Manifeste AppInstaller & Inno Setup
# ==============================================================================
Write-Step "5/5. Génération du fichier .appinstaller et Inno Setup..."

$appInstallerContent = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
    xmlns="http://schemas.microsoft.com/appx/appinstaller/2018"
    Version="$Version"
    Uri="https://github.com/walsoup/fichegen/releases/latest/download/PROFstudio.appinstaller">
    <MainPackage
        Name="PROFstudio.App"
        Publisher="CN=FicheGen"
        Version="$Version"
        ProcessorArchitecture="$Platform"
        Uri="https://github.com/walsoup/fichegen/releases/latest/download/PROFstudio-v$shortVersion-$Platform.msix" />
    <UpdateSettings>
        <OnLaunch HoursBetweenUpdateChecks="12" />
        <AutomaticBackgroundTask />
        <ForceUpdateFromAnyVersion>true</ForceUpdateFromAnyVersion>
    </UpdateSettings>
</AppInstaller>
"@
Set-Content -Path "$distDir\PROFstudio.appinstaller" -Value $appInstallerContent -Encoding Utf8
Write-Success "Manifeste auto-update généré : $distDir\PROFstudio.appinstaller"

# Inno Setup compilation if ISCC is installed
$iscc = $null
$candidatePaths = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
foreach ($p in $candidatePaths) {
    if (Test-Path $p) {
        $iscc = $p
        break
    }
}
if (-not $iscc) {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if ($iscc -and (Test-Path $iscc)) {
    Write-Host "  Compilation avec Inno Setup ($iscc)..." -ForegroundColor Gray
    & $iscc "$repoRoot\installer\PROFstudio.iss"
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Installeur exécutable Inno Setup généré dans $distDir"
    }
} else {
    Write-Host "  (Inno Setup ISCC.exe non détecté. Le script .iss est disponible dans installer/PROFstudio.iss)" -ForegroundColor Gray
}

# ==============================================================================
# Résumé
# ==============================================================================
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "          Installeurs et paquets créés avec succès !        " -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Get-ChildItem -Path $distDir | ForEach-Object {
    $size = if ($_.PSIsContainer) {
        $bytes = (Get-ChildItem -Path $_.FullName -Recurse | Measure-Object -Property Length -Sum).Sum
        "{0:N2} MB (Dossier)" -f ($bytes / 1MB)
    } else {
        "{0:N2} MB" -f ($_.Length / 1MB)
    }
    Write-Host ("  • {0,-42} {1,15}" -f $_.Name, $size) -ForegroundColor White
}
Write-Host ""
Write-Host "Options d'installation pour l'utilisateur :" -ForegroundColor Cyan
Write-Host "  1. Installeur autonome direct : Exécuter Setup.cmd dans artifacts/dist/PROFstudio-Setup-win-x64/" -ForegroundColor Gray
Write-Host "  2. Paquet MSIX : Double-cliquer ou exécuter Install-MSIX.cmd dans artifacts/dist/" -ForegroundColor Gray
Write-Host "  3. Portable : Décompresser PROFstudio-v1.0.0-win-x64-portable.zip et lancer FicheGen.App.exe" -ForegroundColor Gray
Write-Host ""
