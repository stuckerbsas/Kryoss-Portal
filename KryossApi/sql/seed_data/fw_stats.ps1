$cat = Get-Content "$PSScriptRoot\controls_catalog.json" -Raw | ConvertFrom-Json
$imp = Get-Content "$PSScriptRoot\baseline_imperative_checks.json" -Raw | ConvertFrom-Json

$all = @()
foreach ($x in $cat.scored_controls)          { $all += ,@{ src='scored';  fws=$x.frameworks } }
foreach ($x in $cat.baseline_atomic_checks)   { $all += ,@{ src='bl_arr';  fws=$x.frameworks } }
foreach ($x in $imp.imperative_baseline_checks){ $all += ,@{ src='bl_imp';  fws=$x.frameworks } }

Write-Host "Total controls: $($all.Count)"
Write-Host ""
Write-Host "--- Framework coverage (count of controls tagged with each framework) ---"
$fwCount = @{}
foreach ($c in $all) {
    foreach ($f in $c.fws) {
        if (-not $fwCount.ContainsKey($f)) { $fwCount[$f] = 0 }
        $fwCount[$f]++
    }
}
$fwCount.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    Write-Host ("  {0,-10} {1}" -f $_.Key, $_.Value)
}

Write-Host ""
Write-Host "--- Per source x framework ---"
foreach ($src in 'scored','bl_arr','bl_imp') {
    $subset = $all | Where-Object { $_.src -eq $src }
    Write-Host ""
    Write-Host "[$src] total=$($subset.Count)"
    $c = @{}
    foreach ($x in $subset) {
        foreach ($f in $x.fws) {
            if (-not $c.ContainsKey($f)) { $c[$f] = 0 }
            $c[$f]++
        }
    }
    $c.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
        Write-Host ("  {0,-10} {1}" -f $_.Key, $_.Value)
    }
}

Write-Host ""
Write-Host "--- Controls with NO framework tag ---"
$orphans = $all | Where-Object { -not $_.fws -or $_.fws.Count -eq 0 }
Write-Host "  $($orphans.Count)"
