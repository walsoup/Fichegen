$files = Get-ChildItem -Path src, tests -Recurse -Include *.cs,*.xaml | Where-Object { $_.FullName -notmatch "obj|bin" }
$allContent = @{}
foreach ($f in $files) {
    $allContent[$f.FullName] = Get-Content $f.FullName -Raw
}

foreach ($f in $files) {
    $name = $f.BaseName
    if ($name -match "Designer$|ViewModel$|Page$|Window$|App$|Pane$|Host$|Bar$|Dialog$|Button$|Card$|Zone$|AssemblyAttributes$|AssemblyInfo$|Tests$") { continue }
    
    $found = $false
    foreach ($other in $files) {
        if ($other.FullName -ne $f.FullName) {
            if ($allContent[$other.FullName] -match "\b$name\b") {
                $found = $true
                break
            }
        }
    }
    
    if (-not $found) {
        Write-Output "Unreferenced: $($f.FullName)"
    }
}
