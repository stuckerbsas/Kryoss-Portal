$j = Get-Content "$PSScriptRoot\controls_catalog.json" -Raw | ConvertFrom-Json
Write-Host "Scored controls: $($j.scored_controls.Count)"
Write-Host "Baseline checks: $($j.baseline_atomic_checks.Count)"
Write-Host ""
Write-Host "Baseline per parent function:"
$j.baseline_atomic_checks | Group-Object parent_function | Sort-Object Count -Descending | ForEach-Object {
    Write-Host ("  {0,-50} {1}" -f $_.Name, $_.Count)
}
