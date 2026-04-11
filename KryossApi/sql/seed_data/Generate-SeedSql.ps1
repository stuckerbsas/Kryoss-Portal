# Generate-SeedSql.ps1
# Reads controls_catalog.json + baseline_imperative_checks.json and emits
# seed_004_controls.sql with:
#   - ALTER constraint on control_defs.type to allow extended types
#   - INSERTs for missing control_categories
#   - INSERTs for ~605 control_defs
#   - INSERTs for control_frameworks mappings

$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$catalog = Get-Content "$root\controls_catalog.json"           -Raw | ConvertFrom-Json
$imper   = Get-Content "$root\baseline_imperative_checks.json" -Raw | ConvertFrom-Json

$outPath = Join-Path (Split-Path $root -Parent) 'seed_004_controls.sql'

# =========================================================
# Helpers
# =========================================================
function Esc-Sql([string]$s) {
    if ($null -eq $s) { return 'NULL' }
    return "N'" + ($s -replace "'", "''") + "'"
}

function Esc-Json([string]$s) {
    # SQL-escape a JSON string for embedding in an NVARCHAR literal
    if ($null -eq $s) { return 'NULL' }
    return "N'" + ($s -replace "'", "''") + "'"
}

function Normalize-Category([string]$cat) {
    if ([string]::IsNullOrWhiteSpace($cat)) { return 'Uncategorized' }
    # Convert ALL CAPS WITH AMPERSAND into Title Case
    $c = $cat -replace '&', 'and'
    $c = [System.Globalization.CultureInfo]::CurrentCulture.TextInfo.ToTitleCase($c.ToLower())
    return $c
}

function Normalize-Framework([string]$fw) {
    switch ($fw) {
        'ISO'       { 'ISO27001' }
        'PCI'       { 'PCI-DSS' }
        'PCI-DSS'   { 'PCI-DSS' }
        default     { $fw }
    }
}

function Points-To-Severity([int]$points) {
    if ($points -ge 4) { return 'critical' }
    if ($points -eq 3) { return 'high' }
    if ($points -eq 2) { return 'medium' }
    return 'low'
}

function Map-CheckType([string]$t) {
    # Map extended types back to existing schema where possible.
    switch ($t) {
        'registry'          { 'registry' }
        'secedit'           { 'secedit' }
        'auditpol'          { 'auditpol' }
        'firewall'          { 'firewall' }
        'service'           { 'service' }
        'command'           { 'command' }
        'net_accounts'      { 'netaccount' }
        'netaccount'        { 'netaccount' }
        default             { 'command' }   # fallback bucket for extended: user_right, tls, bitlocker, applocker, custom, powershell_cmdlet
    }
}

# =========================================================
# Build category set
# =========================================================
$categorySet = New-Object System.Collections.Generic.HashSet[string]
foreach ($sc in $catalog.scored_controls)         { [void]$categorySet.Add( (Normalize-Category $sc.category) ) }
foreach ($bc in $catalog.baseline_atomic_checks)  { [void]$categorySet.Add( (Normalize-Category $bc.category) ) }
foreach ($ic in $imper.imperative_baseline_checks){ [void]$categorySet.Add( (Normalize-Category $ic.category) ) }

# =========================================================
# Emit SQL
# =========================================================
$sb = New-Object System.Text.StringBuilder

[void]$sb.AppendLine("SET QUOTED_IDENTIFIER ON;")
[void]$sb.AppendLine("SET ANSI_NULLS ON;")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("-- ============================================================")
[void]$sb.AppendLine("-- seed_004_controls.sql")
[void]$sb.AppendLine("-- Kryoss Platform -- Full Control Catalog Seed")
[void]$sb.AppendLine("-- Generated from:")
[void]$sb.AppendLine("--   controls_catalog.json (AST extraction, 161 scored + 155 baseline-array)")
[void]$sb.AppendLine("--   baseline_imperative_checks.json (manual curation, 289 imperative)")
[void]$sb.AppendLine("-- Total: ~605 control_defs")
[void]$sb.AppendLine("-- Run AFTER seed_002_frameworks_platforms.sql")
[void]$sb.AppendLine("-- ============================================================")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("-- Relax type constraint to accept extended check types in check_json")
[void]$sb.AppendLine("IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ctrldef_type')")
[void]$sb.AppendLine("    ALTER TABLE control_defs DROP CONSTRAINT ck_ctrldef_type;")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("ALTER TABLE control_defs ADD CONSTRAINT ck_ctrldef_type CHECK ([type] IN (")
[void]$sb.AppendLine("    'registry','secedit','auditpol','firewall','service','netaccount','command'")
[void]$sb.AppendLine("));")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("-- ============================================================")
[void]$sb.AppendLine("-- Main seed batch (all DECLAREs + all INSERTs in one batch so variables survive)")
[void]$sb.AppendLine("-- ============================================================")
[void]$sb.AppendLine("DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';")
[void]$sb.AppendLine("DECLARE @fwCIS  INT = (SELECT id FROM frameworks WHERE code='CIS');")
[void]$sb.AppendLine("DECLARE @fwNIST INT = (SELECT id FROM frameworks WHERE code='NIST');")
[void]$sb.AppendLine("DECLARE @fwHIPAA INT = (SELECT id FROM frameworks WHERE code='HIPAA');")
[void]$sb.AppendLine("DECLARE @fwPCI  INT = (SELECT id FROM frameworks WHERE code='PCI-DSS');")
[void]$sb.AppendLine("DECLARE @fwISO  INT = (SELECT id FROM frameworks WHERE code='ISO27001');")
[void]$sb.AppendLine("DECLARE @fwCMMC INT = (SELECT id FROM frameworks WHERE code='CMMC');")
[void]$sb.AppendLine("")

# -----------------------------------------------------------
# Categories: insert any missing
# -----------------------------------------------------------
[void]$sb.AppendLine("-- ============================================================")
[void]$sb.AppendLine("-- CATEGORIES (upsert missing)")
[void]$sb.AppendLine("-- ============================================================")
$sortOrder = 100
foreach ($catName in ($categorySet | Sort-Object)) {
    $sortOrder++
    $escName = Esc-Sql $catName
    [void]$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = $escName)")
    [void]$sb.AppendLine("    INSERT INTO control_categories (name, sort_order, created_by) VALUES ($escName, $sortOrder, @systemUserId);")
}
[void]$sb.AppendLine("")

# -----------------------------------------------------------
# control_defs: emit per-row
# -----------------------------------------------------------
[void]$sb.AppendLine("-- ============================================================")
[void]$sb.AppendLine("-- CONTROL DEFS")
[void]$sb.AppendLine("-- ============================================================")

# Track control_id -> framework list so we can emit control_frameworks after
$fwMap = @{}

# ---- SCORED controls (161) ----
$scIdx = 0
foreach ($sc in $catalog.scored_controls) {
    $scIdx++
    $controlId   = 'SC-{0:D3}' -f $scIdx
    $name        = if ($sc.name) { $sc.name } else { $sc.key }
    $description = if ($sc.description) { $sc.description } else { '' }
    $remediation = if ($sc.remediation) { $sc.remediation } else { 'See control documentation' }
    $category    = Normalize-Category $sc.category
    $points      = [int]$sc.points
    $severity    = Points-To-Severity $points
    $sqlType     = 'command'   # scored controls implemented via Test-* functions; agent treats as custom command
    $checkObj    = @{
        check_type = 'scored_function'
        function   = $sc.function
        key        = $sc.key
        points     = $points
        description = $description
    }
    $checkJson   = ($checkObj | ConvertTo-Json -Compress -Depth 5)

    $fullName = "$name ($($sc.function))"
    if ($fullName.Length -gt 300) { $fullName = $fullName.Substring(0,297) + '...' }

    $escCat  = Esc-Sql $category
    $escName = Esc-Sql $fullName
    $escRem  = Esc-Sql $remediation
    $escJson = Esc-Json $checkJson

    [void]$sb.AppendLine("INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)")
    [void]$sb.AppendLine("VALUES ('$controlId', (SELECT id FROM control_categories WHERE name=$escCat), $escName, '$sqlType', '$severity', $escJson, $escRem, 1, 1, @systemUserId);")

    $fwMap[$controlId] = New-Object System.Collections.Generic.List[string]
    foreach ($fw in $sc.frameworks) { [void]$fwMap[$controlId].Add( (Normalize-Framework $fw) ) }
}

# ---- BASELINE ARRAY-PATTERN checks (155) ----
$blIdx = 0
foreach ($bc in $catalog.baseline_atomic_checks) {
    $blIdx++
    $controlId = 'BL-{0:D4}' -f $blIdx
    $parent    = if ($bc.parent_name)     { $bc.parent_name }     else { $bc.parent_function }
    $label     = if ($bc.label)           { $bc.label }           else { "$parent - $($bc.value_name)" }
    $category  = Normalize-Category $bc.category
    $severity  = 'medium'

    $checkObj = @{
        check_type   = 'registry'
        hive         = 'HKLM'
        path         = [string]$bc.path
        value_name   = $bc.value_name
        expected     = $bc.expected
        operator     = if ($bc.operator) { $bc.operator } else { 'eq' }
        parent       = $bc.parent_function
    }
    $checkJson = ($checkObj | ConvertTo-Json -Compress -Depth 5)
    $sqlType = 'registry'

    $fullName = $label
    if ($fullName.Length -gt 300) { $fullName = $fullName.Substring(0,297) + '...' }

    $escCat  = Esc-Sql $category
    $escName = Esc-Sql $fullName
    $escRem  = Esc-Sql "Configure via GPO / registry policy. See CIS/Microsoft baseline."
    $escJson = Esc-Json $checkJson

    [void]$sb.AppendLine("INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)")
    [void]$sb.AppendLine("VALUES ('$controlId', (SELECT id FROM control_categories WHERE name=$escCat), $escName, '$sqlType', '$severity', $escJson, $escRem, 1, 1, @systemUserId);")

    $fwMap[$controlId] = New-Object System.Collections.Generic.List[string]
    foreach ($fw in $bc.frameworks) { [void]$fwMap[$controlId].Add( (Normalize-Framework $fw) ) }
}

# ---- IMPERATIVE baseline checks (289) ----
$imIdx = $blIdx
foreach ($ic in $imper.imperative_baseline_checks) {
    $imIdx++
    $controlId = 'BL-{0:D4}' -f $imIdx
    $parent    = if ($ic.parent_name)     { $ic.parent_name }     else { $ic.parent_function }
    $label     = if ($ic.label)           { $ic.label }           else { $parent }
    $category  = Normalize-Category $ic.category
    $severity  = if ($ic.severity) { $ic.severity } else { 'medium' }

    # Build the check_json object: include all atomic-check fields from the source
    $checkObj = [ordered]@{}
    foreach ($p in $ic.PSObject.Properties) {
        if ($p.Name -in @('parent_function','parent_name','category','frameworks','severity')) { continue }
        $checkObj[$p.Name] = $p.Value
    }
    $checkObj['parent'] = $ic.parent_function

    $checkJson = ($checkObj | ConvertTo-Json -Compress -Depth 6)
    $sqlType   = Map-CheckType $ic.check_type

    $fullName = $label
    if ($fullName.Length -gt 300) { $fullName = $fullName.Substring(0,297) + '...' }

    $escCat  = Esc-Sql $category
    $escName = Esc-Sql $fullName
    $escRem  = Esc-Sql "See parent function: $($ic.parent_function)"
    $escJson = Esc-Json $checkJson

    [void]$sb.AppendLine("INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)")
    [void]$sb.AppendLine("VALUES ('$controlId', (SELECT id FROM control_categories WHERE name=$escCat), $escName, '$sqlType', '$severity', $escJson, $escRem, 1, 1, @systemUserId);")

    $fwMap[$controlId] = New-Object System.Collections.Generic.List[string]
    foreach ($fw in $ic.frameworks) { [void]$fwMap[$controlId].Add( (Normalize-Framework $fw) ) }
}

[void]$sb.AppendLine("")

# -----------------------------------------------------------
# control_frameworks mappings
# -----------------------------------------------------------
[void]$sb.AppendLine("-- ============================================================")
[void]$sb.AppendLine("-- CONTROL_FRAMEWORKS (M:N mappings)")
[void]$sb.AppendLine("-- ============================================================")

foreach ($cid in $fwMap.Keys) {
    $fwList = $fwMap[$cid] | Sort-Object -Unique
    foreach ($fw in $fwList) {
        $fwVar = switch ($fw) {
            'CIS'       { '@fwCIS' }
            'NIST'      { '@fwNIST' }
            'HIPAA'     { '@fwHIPAA' }
            'PCI-DSS'   { '@fwPCI' }
            'ISO27001'  { '@fwISO' }
            'CMMC'      { '@fwCMMC' }
            default     { $null }
        }
        if ($null -eq $fwVar) { continue }
        [void]$sb.AppendLine("INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, $fwVar FROM control_defs WHERE control_id='$cid';")
    }
}
[void]$sb.AppendLine("GO")

# =========================================================
# Write output
# =========================================================
Set-Content -Path $outPath -Value $sb.ToString() -Encoding UTF8
Write-Host "Wrote $outPath"
Write-Host ""
Write-Host "Summary:"
Write-Host "  Scored controls          : $($catalog.scored_controls.Count)"
Write-Host "  Baseline array-pattern   : $($catalog.baseline_atomic_checks.Count)"
Write-Host "  Baseline imperative      : $($imper.imperative_baseline_checks.Count)"
Write-Host "  TOTAL control_defs       : $($catalog.scored_controls.Count + $catalog.baseline_atomic_checks.Count + $imper.imperative_baseline_checks.Count)"
Write-Host "  Categories               : $($categorySet.Count)"
Write-Host "  Framework mappings       : $(($fwMap.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum)"
