$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$localeDir = Join-Path $repoRoot "locales"

if (!(Test-Path -LiteralPath $localeDir)) {
    Write-Error "Locale dizini bulunamadi: $localeDir"
}

$files = Get-ChildItem -LiteralPath $localeDir -Filter "*.json" -File
$failed = $false

foreach ($file in $files) {
    try {
        Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName | ConvertFrom-Json | Out-Null
        Write-Host "[OK] $($file.Name)"
    }
    catch {
        $failed = $true
        Write-Host "[ERROR] $($file.Name): $($_.Exception.Message)"
    }
}

if ($failed) {
    exit 1
}

Write-Host "Tum locale dosyalari gecerli."
