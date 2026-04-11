$j = Get-Content "$PSScriptRoot\controls_catalog.json" -Raw | ConvertFrom-Json
Write-Host "--- atomic check sample ---"
$j.baseline_atomic_checks[0] | ConvertTo-Json -Depth 5
Write-Host "--- scored control sample ---"
$j.scored_controls[0] | ConvertTo-Json -Depth 5
Write-Host "--- unique categories (scored) ---"
$j.scored_controls.category | Sort-Object -Unique
Write-Host "--- unique categories (baseline array) ---"
$j.baseline_atomic_checks.category | Sort-Object -Unique
Write-Host "--- unique frameworks (scored) ---"
$j.scored_controls.frameworks | Sort-Object -Unique
