param(
    [Parameter(Mandatory=$true)]
    [string]$TargetDir
)

if (-not (Test-Path -LiteralPath $TargetDir)) {
    exit 0
}

# Locate signtool.exe
$sdkBin = "C:\Program Files (x86)\Windows Kits\10\bin"
$signtool = $null
if (Test-Path -LiteralPath $sdkBin) {
    $signtool = (Get-ChildItem -Path $sdkBin -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*x64*" } | Select-Object -First 1).FullName
}
if (-not $signtool -or -not (Test-Path -LiteralPath $signtool)) {
    $defaultSigntool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
    if (Test-Path -LiteralPath $defaultSigntool) {
        $signtool = $defaultSigntool
    }
}

if (-not $signtool) {
    exit 0
}

# Locate CN=FicheGen certificate
$cert = Get-ChildItem -Path "Cert:\CurrentUser\My" -ErrorAction SilentlyContinue | Where-Object { $_.Subject -eq "CN=FicheGen" } | Select-Object -First 1
if (-not $cert) {
    $cert = Get-ChildItem -Path "Cert:\LocalMachine\My" -ErrorAction SilentlyContinue | Where-Object { $_.Subject -eq "CN=FicheGen" } | Select-Object -First 1
}

if (-not $cert) {
    exit 0
}

# Sign only our owned project binaries (FicheGen.*) — NEVER overwrite or modify vendor/Microsoft NuGet assemblies.
$files = @(Get-ChildItem -Path $TargetDir -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
    ($_.Extension -in '.dll', '.exe') -and 
    ($_.Name -like 'FicheGen*')
})
if ($files.Count -gt 0) {
    $chunkSize = 30
    for ($idx = 0; $idx -lt $files.Count; $idx += $chunkSize) {
        $chunk = $files[$idx..([Math]::Min($idx + $chunkSize - 1, $files.Count - 1))]
        $chunkPaths = @($chunk | ForEach-Object { $_.FullName })
        & $signtool sign /s my /fd SHA256 /sha1 $cert.Thumbprint $chunkPaths 2>$null | Out-Null
    }
}
