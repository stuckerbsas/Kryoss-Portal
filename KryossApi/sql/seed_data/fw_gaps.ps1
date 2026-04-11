$cat = Get-Content "$PSScriptRoot\controls_catalog.json" -Raw | ConvertFrom-Json
$imp = Get-Content "$PSScriptRoot\baseline_imperative_checks.json" -Raw | ConvertFrom-Json

# Build unified list
$all = New-Object System.Collections.Generic.List[object]
$idx = 0
foreach ($x in $cat.scored_controls) {
    $idx++
    $all.Add([PSCustomObject]@{
        id     = 'SC-{0:D3}' -f $idx
        src    = 'scored'
        name   = $x.name
        fun    = $x.function
        cat    = $x.category
        fws    = @($x.frameworks)
    })
}
$bidx = 0
foreach ($x in $cat.baseline_atomic_checks) {
    $bidx++
    $all.Add([PSCustomObject]@{
        id     = 'BL-{0:D4}' -f $bidx
        src    = 'bl_arr'
        name   = $x.label
        fun    = $x.parent_function
        cat    = $x.category
        fws    = @($x.frameworks)
    })
}
foreach ($x in $imp.imperative_baseline_checks) {
    $bidx++
    $all.Add([PSCustomObject]@{
        id     = 'BL-{0:D4}' -f $bidx
        src    = 'bl_imp'
        name   = $x.label
        fun    = $x.parent_function
        cat    = $x.category
        fws    = @($x.frameworks)
    })
}

Write-Host "============================================================"
Write-Host "CONTROLES SIN TAG CIS"
Write-Host "============================================================"
$noCis = $all | Where-Object { $_.fws -notcontains 'CIS' }
Write-Host "Total: $($noCis.Count)"
Write-Host ""
$noCis | ForEach-Object {
    Write-Host ("[{0}] {1}" -f $_.id, $_.name)
    Write-Host ("        src={0}  fws={1}  parent={2}" -f $_.src, ($_.fws -join ','), $_.fun)
}

Write-Host ""
Write-Host "============================================================"
Write-Host "CONTROLES SIN TAG NIST"
Write-Host "============================================================"
$noNist = $all | Where-Object { $_.fws -notcontains 'NIST' }
Write-Host "Total: $($noNist.Count)"
Write-Host ""
$noNist | ForEach-Object {
    Write-Host ("[{0}] {1}" -f $_.id, $_.name)
    Write-Host ("        src={0}  fws={1}  parent={2}" -f $_.src, ($_.fws -join ','), $_.fun)
}

Write-Host ""
Write-Host "============================================================"
Write-Host "HIPAA COVERAGE"
Write-Host "============================================================"
$hipaa = $all | Where-Object { $_.fws -contains 'HIPAA' }
$noHipaa = $all | Where-Object { $_.fws -notcontains 'HIPAA' }
Write-Host "Controles CON HIPAA: $($hipaa.Count)"
Write-Host "Controles SIN HIPAA: $($noHipaa.Count)"
Write-Host ""
Write-Host "Distribucion de categorias SIN tag HIPAA (estos son candidatos a evaluar):"
$noHipaa | Group-Object cat | Sort-Object Count -Descending | ForEach-Object {
    Write-Host ("  {0,-45} {1}" -f $_.Name, $_.Count)
}
