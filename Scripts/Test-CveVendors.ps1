param([string]$CvePath = "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\cvelistV5-main")

Write-Host "PS Version: $($PSVersionTable.PSVersion)"

$vendors = @{}
$total = 0
$noVendor = 0
$errors = 0

$cvesRoot = Join-Path $CvePath "cves\2025\21xxx"
$files = Get-ChildItem -Path $cvesRoot -Filter "CVE-*.json" -File | Select-Object -First 100

foreach ($f in $files) {
    $total++
    try {
        $raw = Get-Content $f.FullName -Raw -Encoding UTF8
        $cve = $raw | ConvertFrom-Json
        $v = $cve.containers.cna.affected[0].vendor
        if ($v) {
            if ($vendors.ContainsKey($v)) { $vendors[$v]++ } else { $vendors[$v] = 1 }
        } else {
            $noVendor++
        }
    } catch {
        $errors++
        if ($errors -le 3) { Write-Host "ERR $($f.Name): $($_.Exception.Message)" }
    }
}

Write-Host "Total: $total  NoVendor: $noVendor  Errors: $errors"
Write-Host ""
$vendors.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { Write-Host "$($_.Key): $($_.Value)" }
