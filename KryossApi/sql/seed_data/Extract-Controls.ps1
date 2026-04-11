<#
.SYNOPSIS
    Extracts security control definitions from Invoke-KryossAssessment.ps1 into a JSON catalog.

.DESCRIPTION
    Uses PowerShell AST parser to walk the assessment script and extract:
      1. The $CONTROL_WEIGHTS hashtable (with category comments).
      2. Each Test-* function's [PSCustomObject]@{...} result (Name, Description, Remediation, Category).
      3. Atomic baseline checks inside Test-Baseline* functions ($checks array of hashtables).

    Output: controls_catalog.json in the same directory as this script.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-07
#>

[CmdletBinding()]
param(
    [string]$SourceScript = "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\Scripts\Audit\Invoke-KryossAssessment.ps1",
    [string]$OutputJson = "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\KryossApi\sql\seed_data\controls_catalog.json"
)

$ErrorActionPreference = 'Stop'

Write-Host "Parsing source script: $SourceScript"

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($SourceScript, [ref]$tokens, [ref]$parseErrors)

if ($parseErrors -and $parseErrors.Count -gt 0) {
    Write-Warning "Parser produced $($parseErrors.Count) errors (continuing)."
}

# ---------------------------------------------------------------
# Helper: extract string value from AST expression (best effort)
# ---------------------------------------------------------------
function Get-StringValue {
    param($Expr)
    if ($null -eq $Expr) { return $null }
    if ($Expr -is [System.Management.Automation.Language.StringConstantExpressionAst]) {
        return $Expr.Value
    }
    if ($Expr -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
        return $Expr.Value
    }
    if ($Expr -is [System.Management.Automation.Language.ConstantExpressionAst]) {
        return [string]$Expr.Value
    }
    # Fallback: use Extent text
    try { return $Expr.Extent.Text.Trim("'",'"') } catch { return $null }
}

function Get-IntValue {
    param($Expr)
    if ($null -eq $Expr) { return $null }
    if ($Expr -is [System.Management.Automation.Language.ConstantExpressionAst]) {
        return $Expr.Value
    }
    try { return $Expr.Extent.Text } catch { return $null }
}

function Get-ArrayStrings {
    param($Expr)
    $out = @()
    if ($null -eq $Expr) { return ,$out }
    if ($Expr -is [System.Management.Automation.Language.ArrayLiteralAst]) {
        foreach ($e in $Expr.Elements) {
            $v = Get-StringValue $e
            if ($v) { $out += $v }
        }
    } elseif ($Expr -is [System.Management.Automation.Language.ArrayExpressionAst]) {
        foreach ($stmt in $Expr.SubExpression.Statements) {
            if ($stmt.PipelineElements) {
                foreach ($pe in $stmt.PipelineElements) {
                    if ($pe.Expression -is [System.Management.Automation.Language.ArrayLiteralAst]) {
                        foreach ($e in $pe.Expression.Elements) {
                            $v = Get-StringValue $e
                            if ($v) { $out += $v }
                        }
                    } else {
                        $v = Get-StringValue $pe.Expression
                        if ($v) { $out += $v }
                    }
                }
            }
        }
    } else {
        $v = Get-StringValue $Expr
        if ($v) { $out += $v }
    }
    return ,$out
}

# ---------------------------------------------------------------
# STEP 1: Find $CONTROL_WEIGHTS assignment and collect category map
# ---------------------------------------------------------------
Write-Host "`n[1/3] Locating `$CONTROL_WEIGHTS..."

$controlWeightsAssign = $ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
    $node.Left.Extent.Text -eq '$CONTROL_WEIGHTS'
}, $true) | Select-Object -First 1

if (-not $controlWeightsAssign) {
    throw "Could not find `$CONTROL_WEIGHTS assignment in source."
}

$hashtableAst = $controlWeightsAssign.Right.Find({
    param($node)
    $node -is [System.Management.Automation.Language.HashtableAst]
}, $true)

if (-not $hashtableAst) {
    throw "Could not find hashtable AST inside `$CONTROL_WEIGHTS."
}

Write-Host "  Found `$CONTROL_WEIGHTS at line $($controlWeightsAssign.Extent.StartLineNumber), with $($hashtableAst.KeyValuePairs.Count) entries."

# Build a map of line number -> category, using comment tokens inside the hashtable extent
$htStart = $hashtableAst.Extent.StartLineNumber
$htEnd   = $hashtableAst.Extent.EndLineNumber

$categoryComments = $tokens | Where-Object {
    $_.Kind -eq 'Comment' -and
    $_.Extent.StartLineNumber -ge $htStart -and
    $_.Extent.EndLineNumber   -le $htEnd
}

# The section format is three-line: "# ===", "# CATEGORY NAME (...)", "# ===".
# We'll parse category from any comment line that is not just a divider, not trailing (e.g. "# INFO only").
$categoryByLine = @{}
$currentCategory = 'Uncategorized'

# Sort comments by line
$sortedComments = $categoryComments | Sort-Object { $_.Extent.StartLineNumber }

foreach ($c in $sortedComments) {
    $text = $c.Text.TrimStart('#').Trim()
    # Strip leading/trailing box-drawing / equals
    $clean = $text -replace '^[=\u2550\s]+','' -replace '[=\u2550\s]+$',''
    if ([string]::IsNullOrWhiteSpace($clean)) { continue }
    # Category lines MUST contain the "(N controls ...)" marker to be considered section headers.
    # This avoids false positives from inline trailing comments like "# INFO only".
    if ($clean -match '\(\s*\d+\s*controls?') {
        $cat = $clean -replace '\s*\(\s*\d+\s*controls?.*$',''
        $currentCategory = ($cat -replace '\s+',' ').Trim()
    }
    $categoryByLine[$c.Extent.StartLineNumber] = $currentCategory
}

# For each hashtable key, find the nearest preceding category line
$weightsByKey = [ordered]@{}
foreach ($kvp in $hashtableAst.KeyValuePairs) {
    $keyName = Get-StringValue $kvp.Item1
    if (-not $keyName) { continue }

    $line = $kvp.Item1.Extent.StartLineNumber

    # Walk backwards from this line to find a known category
    $cat = 'Uncategorized'
    for ($ln = $line; $ln -ge $htStart; $ln--) {
        if ($categoryByLine.ContainsKey($ln)) {
            $cat = $categoryByLine[$ln]
            break
        }
    }

    # Parse value hashtable: @{ Points = N; Frameworks = @(...) }
    $valueHt = $kvp.Item2.Find({
        param($n) $n -is [System.Management.Automation.Language.HashtableAst]
    }, $true)

    $points = 0
    $frameworks = @()
    if ($valueHt) {
        foreach ($p in $valueHt.KeyValuePairs) {
            $pn = Get-StringValue $p.Item1
            if ($pn -eq 'Points') {
                $v = $p.Item2.Find({ param($n) $n -is [System.Management.Automation.Language.ConstantExpressionAst] }, $true)
                if ($v) { $points = [int]$v.Value }
            } elseif ($pn -eq 'Frameworks') {
                $frameworks = Get-ArrayStrings $p.Item2.PipelineElements[0].Expression
            }
        }
    }

    $weightsByKey[$keyName] = [PSCustomObject]@{
        key        = $keyName
        points     = $points
        frameworks = $frameworks
        category   = $cat
    }
}

Write-Host "  Extracted $($weightsByKey.Count) weighted control entries."

# ---------------------------------------------------------------
# STEP 2: Walk all Test-* functions, extract PSCustomObject fields
# ---------------------------------------------------------------
Write-Host "`n[2/3] Walking Test-* functions..."

$allFunctions = $ast.FindAll({
    param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true)

$testFunctions = $allFunctions | Where-Object {
    $_.Name -like 'Test-*' -and $_.Name -ne 'Test-ControlWeightsIntegrity'
}

Write-Host "  Found $($testFunctions.Count) Test-* functions."

# Helper to extract PSCustomObject hashtable inside a function
function Get-PSCustomObjectFields {
    param($FunctionAst)
    $pscoHt = $null
    # Find all ConvertExpressionAst / hashtables following [PSCustomObject]
    $convertNodes = $FunctionAst.FindAll({
        param($n) $n -is [System.Management.Automation.Language.ConvertExpressionAst]
    }, $true)
    foreach ($cn in $convertNodes) {
        $typeName = $cn.Type.TypeName.FullName
        if ($typeName -match 'PSCustomObject|psobject') {
            $child = $cn.Find({ param($x) $x -is [System.Management.Automation.Language.HashtableAst] }, $true)
            if ($child) { $pscoHt = $child; break }
        }
    }
    if (-not $pscoHt) { return $null }

    $fields = @{}
    foreach ($kvp in $pscoHt.KeyValuePairs) {
        $k = Get-StringValue $kvp.Item1
        if (-not $k) { continue }
        # Get the expression from the StatementBlockAst's pipeline
        $expr = $null
        if ($kvp.Item2.PipelineElements -and $kvp.Item2.PipelineElements.Count -gt 0) {
            $expr = $kvp.Item2.PipelineElements[0].Expression
        }
        if ($null -eq $expr) { continue }

        switch -Regex ($k) {
            '^(Name|Description|Remediation|Category|Finding|Status)$' {
                $fields[$k] = Get-StringValue $expr
            }
            '^Frameworks$' {
                if ($expr -is [System.Management.Automation.Language.ArrayLiteralAst] -or
                    $expr -is [System.Management.Automation.Language.ArrayExpressionAst]) {
                    $fields[$k] = Get-ArrayStrings $expr
                }
            }
            '^(Points|Score|MaxScore)$' {
                if ($expr -is [System.Management.Automation.Language.ConstantExpressionAst]) {
                    $fields[$k] = $expr.Value
                }
            }
            default { }
        }
    }
    return $fields
}

# Match function name to weights key
function Resolve-WeightsKey {
    param([string]$FunctionName, $WeightsMap)
    $stripped = $FunctionName -replace '^Test-',''

    if ($WeightsMap.Contains($stripped)) { return $stripped }

    # Manual mapping hints from the header comment block
    $hints = @{
        'CertificateStore'      = 'ExpiringCertificates'
        'WeakCrypto'            = 'WeakTLSProtocols'
        'SSLCertificates'       = 'UntrustedRootCAs'
        'UnsignedSoftware'      = 'BlacklistedSoftware'
        'OutdatedSoftware'      = 'EOLSoftware'
        'SuspiciousProcesses'   = 'SuspiciousProcesses'
        'SharedFolders'         = 'AdminShares'
        'NTFSPermissions'       = 'SensitiveFolderPermissions'
        'WindowsDefender'       = 'Defender'
        'WindowsFirewall'       = 'Firewall'
        'PowerShellPolicy'      = 'PSExecPolicy'
        'WDigestAuthentication' = 'WDigestAuth'
    }
    if ($hints.ContainsKey($stripped)) { return $hints[$stripped] }

    # Case-insensitive fallback
    foreach ($k in $WeightsMap.Keys) {
        if ($k -ieq $stripped) { return $k }
    }
    return $null
}

$scoredControls = @()
$parseFailures  = @()

foreach ($fn in $testFunctions) {
    try {
        $fields = Get-PSCustomObjectFields -FunctionAst $fn
        $weightsKey = Resolve-WeightsKey -FunctionName $fn.Name -WeightsMap $weightsByKey

        $wentry = $null
        if ($weightsKey -and $weightsByKey.Contains($weightsKey)) {
            $wentry = $weightsByKey[$weightsKey]
        }

        $name        = $null
        $description = $null
        $remediation = $null
        $category    = $null
        $frameworks  = @()
        $points      = 0

        if ($fields) {
            if ($fields.ContainsKey('Name'))        { $name        = $fields['Name'] }
            if ($fields.ContainsKey('Description')) { $description = $fields['Description'] }
            if ($fields.ContainsKey('Remediation')) { $remediation = $fields['Remediation'] }
            if ($fields.ContainsKey('Category'))    { $category    = $fields['Category'] }
            if ($fields.ContainsKey('Frameworks'))  { $frameworks  = $fields['Frameworks'] }
            if ($fields.ContainsKey('Points'))      { $points      = [int]$fields['Points'] }
        }

        if ($wentry) {
            if (-not $frameworks -or $frameworks.Count -eq 0) { $frameworks = $wentry.frameworks }
            if ($points -le 0) { $points = $wentry.points }
            if (-not $category) { $category = $wentry.category }
        }

        if (-not $name)        { $name        = ($fn.Name -replace '^Test-','') }
        if (-not $description) { $description = $name }
        if (-not $remediation) { $remediation = 'See control documentation' }
        if (-not $category)    { $category    = 'Uncategorized' }
        if (-not $frameworks)  { $frameworks  = @() }

        $checkType = if ($fn.Name -like 'Test-Baseline*') { 'baseline_registry_batch' } else { 'powershell_cmdlet' }

        $scoredControls += [PSCustomObject]@{
            key         = if ($weightsKey) { $weightsKey } else { ($fn.Name -replace '^Test-','') }
            function    = $fn.Name
            name        = $name
            description = $description
            remediation = $remediation
            category    = $category
            points      = $points
            frameworks  = $frameworks
            check_type  = $checkType
        }
    }
    catch {
        $parseFailures += [PSCustomObject]@{
            function = $fn.Name
            error    = $_.Exception.Message
        }
    }
}

Write-Host "  Extracted $($scoredControls.Count) scored controls; $($parseFailures.Count) failures."

# ---------------------------------------------------------------
# STEP 3: For Test-Baseline* functions, extract atomic checks
# ---------------------------------------------------------------
Write-Host "`n[3/3] Extracting atomic baseline checks..."

$baselineChecks = @()
$baselineFunctions = $testFunctions | Where-Object { $_.Name -like 'Test-Baseline*' }

foreach ($fn in $baselineFunctions) {
    try {
        # Look for $checks = @( @{...}, @{...} ) assignment
        $checksAssigns = $fn.FindAll({
            param($n)
            $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $n.Left.Extent.Text -match '^\$checks$'
        }, $true)

        if (-not $checksAssigns) { continue }

        # Derive parent context from scoredControls
        $parent = $scoredControls | Where-Object { $_.function -eq $fn.Name } | Select-Object -First 1
        $parentName = if ($parent) { $parent.name } else { $fn.Name }
        $parentCategory = if ($parent) { $parent.category } else { 'Windows Security Baseline' }
        $parentFrameworks = if ($parent) { $parent.frameworks } else { @() }

        foreach ($assign in $checksAssigns) {
            # Collect all hashtable literals inside the assigned array
            $hashtables = $assign.Right.FindAll({
                param($n) $n -is [System.Management.Automation.Language.HashtableAst]
            }, $true)

            foreach ($ht in $hashtables) {
                $entry = @{}
                foreach ($kvp in $ht.KeyValuePairs) {
                    $k = Get-StringValue $kvp.Item1
                    if (-not $k) { continue }
                    $expr = $null
                    if ($kvp.Item2.PipelineElements -and $kvp.Item2.PipelineElements.Count -gt 0) {
                        $expr = $kvp.Item2.PipelineElements[0].Expression
                    }
                    if ($null -eq $expr) { continue }

                    switch -Regex ($k) {
                        '^(Path|Name|Label|Op|Type)$' {
                            $entry[$k] = Get-StringValue $expr
                        }
                        '^Expected$' {
                            $v = Get-StringValue $expr
                            if ($null -eq $v) {
                                $ce = $expr.Find({ param($n) $n -is [System.Management.Automation.Language.ConstantExpressionAst] }, $true)
                                if ($ce) { $v = $ce.Value }
                            }
                            $entry['Expected'] = $v
                        }
                        default { }
                    }
                }

                # Only add if this looks like a baseline check (must have Path/Name OR Label)
                if ($entry.ContainsKey('Path') -or $entry.ContainsKey('Label')) {
                    $baselineChecks += [PSCustomObject]@{
                        parent_function  = $fn.Name
                        parent_name      = $parentName
                        category         = $parentCategory
                        frameworks       = $parentFrameworks
                        path             = $entry['Path']
                        value_name       = $entry['Name']
                        expected         = $entry['Expected']
                        operator         = $entry['Op']
                        label            = $entry['Label']
                    }
                }
            }
        }
    }
    catch {
        $parseFailures += [PSCustomObject]@{
            function = "$($fn.Name) [baseline]"
            error    = $_.Exception.Message
        }
    }
}

Write-Host "  Extracted $($baselineChecks.Count) atomic baseline checks."

# ---------------------------------------------------------------
# Output
# ---------------------------------------------------------------
$outDir = Split-Path -Parent $OutputJson
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$catalog = [PSCustomObject]@{
    generated_at            = (Get-Date).ToString('s')
    source_script           = $SourceScript
    scored_controls         = $scoredControls
    baseline_atomic_checks  = $baselineChecks
    parse_failures          = $parseFailures
}

$json = $catalog | ConvertTo-Json -Depth 10
Set-Content -Path $OutputJson -Value $json -Encoding UTF8

Write-Host "`n=========================================="
Write-Host "EXTRACTION SUMMARY"
Write-Host "=========================================="
Write-Host "Scored controls:         $($scoredControls.Count)"
Write-Host "Baseline atomic checks:  $($baselineChecks.Count)"
Write-Host "Parse failures:          $($parseFailures.Count)"
Write-Host "Output file:             $OutputJson"
Write-Host "=========================================="

if ($parseFailures.Count -gt 0) {
    Write-Host "`nFailures:"
    $parseFailures | Format-Table -AutoSize | Out-String | Write-Host
}
