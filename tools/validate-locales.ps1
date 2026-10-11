$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$localeDir = Join-Path $repoRoot "locales"

if (!(Test-Path -LiteralPath $localeDir)) {
    Write-Error "Locale dizini bulunamadi: $localeDir"
}

$files = Get-ChildItem -LiteralPath $localeDir -Filter "*.json" -File
$failed = $false

# E-3: en.json baz alan olarak alınır; tüm diğer locale'ler aynı anahtar kümesine sahip olmalı
$enFile = $files | Where-Object { $_.Name -eq "en.json" }
if ($null -eq $enFile) {
    Write-Error "Referans dosya en.json bulunamadi."
}

$referenceKeys = @()
try {
    $enJson = Get-Content -Raw -Encoding UTF8 -LiteralPath $enFile.FullName | ConvertFrom-Json
    $referenceKeys = @($enJson.PSObject.Properties.Name | Sort-Object)
    Write-Host "[REF] en.json: $($referenceKeys.Count) anahtar"
}
catch {
    $failed = $true
    Write-Host "[ERROR] en.json: $($_.Exception.Message)"
}

foreach ($file in $files) {
    try {
        $json = Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName | ConvertFrom-Json
        $keys = @($json.PSObject.Properties.Name | Sort-Object)

        # Key-parite kontrolü (en.json kendisi hariç)
        if ($file.Name -ne "en.json" -and $referenceKeys.Count -gt 0) {
            $missing = @($referenceKeys | Where-Object { $keys -notcontains $_ })
            $extra   = @($keys | Where-Object { $referenceKeys -notcontains $_ })

            if ($missing.Count -gt 0) {
                # Eksik anahtarlar runtime'da sessizce Ingilizce fallback ile calisir; build'i bloklamaz ama gorunir olmali
                Write-Host "[WARN] $($file.Name): eksik anahtarlar (En fallback devrede) -> $($missing -join ', ')"
            }
            elseif ($extra.Count -gt 0) {
                # Fazla anahtar hata degil ama uyari ver (sessiz Ingilizce fallback riski dusuk)
                Write-Host "[WARN] $($file.Name): referansta olmayan anahtarlar -> $($extra -join ', ')"
            }
            else {
                Write-Host "[OK] $($file.Name) ($($keys.Count) anahtar, parite tamam)"
            }
        }
        else {
            Write-Host "[OK] $($file.Name) ($($keys.Count) anahtar)"
        }
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
