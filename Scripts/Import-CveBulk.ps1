<#
.SYNOPSIS
    Bulk import CVE records from CVE.org cvelistV5 into KryossDb.

.DESCRIPTION
    Reads all CVE JSON files from the cvelistV5 bulk download and inserts/updates
    cve_entries in Azure SQL. Extracts vendor, product, affected versions, description,
    CVSS scores (from ADP enrichment), and CWE IDs.

    Designed for one-time initial load. Safe to re-run (upserts by cve_id).

.PARAMETER CvePath
    Root path of the extracted cvelistV5 repo (contains /cves/ folder).

.PARAMETER BatchSize
    Number of records per SQL batch. Default 500.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-29

.EXAMPLE
    .\Import-CveBulk.ps1 -CvePath "C:\path\to\cvelistV5-main"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CvePath,

    [int]$BatchSize = 500,

    [string]$VendorFile
)

$ErrorActionPreference = 'Stop'

# Normalize vendor name: lowercase, strip spaces/underscores/hyphens + corporate suffixes (repeated to peel layers)
function Normalize-VendorName([string]$name) {
    $n = $name.ToLowerInvariant() -replace '[\s_\-\.]+',''
    $suffixes = '(,?inc|corporation|corp|ltd|systems|technologies|incorporated|enterprise|software|foundation|group|gmbh|ag)$'
    $n = $n -replace $suffixes,''
    $n = $n -replace $suffixes,''
    return $n
}

# Load vendor whitelist from shared JSON (single source of truth with CveSyncService.cs)
$VENDOR_SET = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

if (-not $VendorFile) {
    $VendorFile = Join-Path $PSScriptRoot "..\KryossApi\src\KryossApi\cve-vendors.json"
}

if (Test-Path $VendorFile) {
    $vendors = Get-Content $VendorFile -Raw | ConvertFrom-Json
    foreach ($v in $vendors) { $VENDOR_SET.Add((Normalize-VendorName $v)) | Out-Null }
    Write-Host "Loaded $($VENDOR_SET.Count) allowed vendors from $VendorFile"
} else {
    @('microsoft','apple','google','mozilla','oracle','cisco','vmware','adobe',
      'dell','hp','hpe','lenovo','intel','fortinet','sonicwall','sophos',
      'paloaltonetworks','watchguard','barracuda','ubiquiti','veeam','acronis',
      'qnap','synology','teamviewer','connectwise','zoom','citrix','broadcom'
    ) | ForEach-Object { $VENDOR_SET.Add($_) | Out-Null }
    Write-Host "WARNING: Vendor file not found at $VendorFile, using hardcoded fallback ($($VENDOR_SET.Count) vendors)" -ForegroundColor Yellow
}

$LOG_DIR = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE = "Import-CveBulk_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"
if (-not (Test-Path -Path $LOG_DIR)) { New-Item -Path $LOG_DIR -ItemType Directory -Force | Out-Null }
$logPath = Join-Path -Path $LOG_DIR -ChildPath $LOG_FILE

function Write-Log {
    param([Parameter(Mandatory)][string]$Message, [ValidateSet("INFO","WARN","ERROR")][string]$Level = "INFO")
    $entry = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [$Level] $Message"
    Write-Host $entry
    Add-Content -Path $logPath -Value $entry
}

# Get Azure SQL token
Write-Log "Acquiring Azure SQL access token..."
$token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv 2>$null
if (-not $token) {
    Write-Log "Failed to get Azure SQL token. Run 'az login' first." -Level "ERROR"
    exit 1
}

$connStr = "Server=tcp:sql-kryoss.database.windows.net,1433;Database=KryossDb;Encrypt=True;TrustServerCertificate=False;"
$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = $connStr
$conn.AccessToken = $token
$conn.Open()
Write-Log "Connected to Azure SQL"

# Prepare merge statement
$mergeSql = @"
MERGE cve_entries AS target
USING (VALUES (@cveId, @vendor, @product, @productClass, @productPattern, @severity, @cvssScore, @description, @cweId, @publishedAt, @cpeMatchString, @affectedBelow, @fixedVersion, @referencesUrl))
AS source (cve_id, vendor, product, product_class, product_pattern, severity, cvss_score, description, cwe_id, published_at, cpe_match_string, affected_below, fixed_version, references_url)
ON target.cve_id = source.cve_id
WHEN MATCHED THEN UPDATE SET
    target.vendor = COALESCE(source.vendor, target.vendor),
    target.product = COALESCE(source.product, target.product),
    target.product_class = COALESCE(source.product_class, target.product_class),
    target.product_pattern = COALESCE(source.product_pattern, target.product_pattern),
    target.severity = CASE WHEN source.cvss_score IS NOT NULL THEN source.severity ELSE target.severity END,
    target.cvss_score = COALESCE(source.cvss_score, target.cvss_score),
    target.description = COALESCE(source.description, target.description),
    target.cwe_id = COALESCE(source.cwe_id, target.cwe_id),
    target.cpe_match_string = COALESCE(source.cpe_match_string, target.cpe_match_string),
    target.affected_below = COALESCE(source.affected_below, target.affected_below),
    target.fixed_version = COALESCE(source.fixed_version, target.fixed_version),
    target.references_url = COALESCE(source.references_url, target.references_url),
    target.updated_at = GETUTCDATE()
WHEN NOT MATCHED THEN INSERT
    (cve_id, vendor, product, product_class, product_pattern, severity, cvss_score, description, cwe_id, published_at, source, cpe_match_string, affected_below, fixed_version, references_url, created_at, updated_at)
VALUES
    (source.cve_id, source.vendor, source.product, source.product_class, source.product_pattern, source.severity, source.cvss_score, source.description, source.cwe_id, source.published_at, 'cve.org', source.cpe_match_string, source.affected_below, source.fixed_version, source.references_url, GETUTCDATE(), GETUTCDATE());
"@

function Get-SeverityFromScore([decimal]$score) {
    if ($score -ge 9.0) { return "critical" }
    if ($score -ge 7.0) { return "high" }
    if ($score -ge 4.0) { return "medium" }
    return "low"
}

# Resolve vendor: affected[].vendor (all entries) → providerMetadata.shortName
function Resolve-CveVendor($cna) {
    $affected = $cna.affected
    if ($affected) {
        foreach ($entry in $affected) {
            $v = $entry.vendor
            if ($v -and $VENDOR_SET.Contains((Normalize-VendorName $v))) { return $v }
        }
    }
    $shortName = $cna.providerMetadata.shortName
    if ($shortName -and $VENDOR_SET.Contains((Normalize-VendorName $shortName))) { return $shortName }
    return $null
}

# Classify product: OS / PLATFORM / APPLICATION / LIBRARY
function Get-ProductClass([string]$product) {
    if (-not $product) { return 'APPLICATION' }
    $p = $product.ToLowerInvariant()
    if ($p -match 'windows|macos|ios|android|chromeos|linux|ubuntu|debian|red hat enterprise') { return 'OS' }
    if ($p -match '\.net|java|node\.?js|python|php|ruby') { return 'PLATFORM' }
    if ($p -match 'openssl|zlib|curl|libxml|sqlite|libjpeg|libpng|freetype|expat') { return 'LIBRARY' }
    return 'APPLICATION'
}

function Parse-CveRecord([string]$jsonPath) {
    try {
        $raw = Get-Content -Path $jsonPath -Raw -Encoding UTF8
        $cve = $raw | ConvertFrom-Json

        if ($cve.cveMetadata.state -ne "PUBLISHED") { return $null }

        $cna = $cve.containers.cna
        if (-not $cna) { return $null }

        # Multi-field vendor resolution
        $vendor = Resolve-CveVendor $cna
        if (-not $vendor) { return $null }

        $cveId = $cve.cveMetadata.cveId
        $published = $cve.cveMetadata.datePublished

        # Description
        $descObj = $cna.descriptions | Where-Object { $_.lang -like "en*" } | Select-Object -First 1
        $desc = if ($descObj) { $descObj.value } else { $null }
        if (-not $desc) {
            $descObj = $cna.descriptions | Select-Object -First 1
            $desc = if ($descObj) { $descObj.value } else { $null }
        }
        if ($desc -and $desc.Length -gt 4000) { $desc = $desc.Substring(0, 4000) }

        # Product + versions from the matched vendor's affected entry
        $product = $null
        $affectedBelow = $null
        $normalizedVendor = Normalize-VendorName $vendor

        $affected = $cna.affected
        if ($affected) {
            $matchedEntry = $null
            foreach ($entry in $affected) {
                if ($entry.vendor -and (Normalize-VendorName $entry.vendor) -eq $normalizedVendor) {
                    $matchedEntry = $entry
                    break
                }
            }
            # Fallback: if vendor came from providerMetadata, use first affected entry
            if (-not $matchedEntry -and $affected.Count -gt 0) { $matchedEntry = $affected[0] }

            if ($matchedEntry) {
                $product = $matchedEntry.product
                foreach ($v in $matchedEntry.versions) {
                    if ($v.lessThan) { $affectedBelow = $v.lessThan; break }
                    if ($v.lessThanOrEqual) { $affectedBelow = $v.lessThanOrEqual; break }
                }
            }
        }

        $productClass = Get-ProductClass $product
        $productPattern = "%$($vendor)%"

        # References
        $refObj = $cna.references | Select-Object -First 1
        $refUrl = if ($refObj) { $refObj.url } else { $null }
        if ($refUrl -and $refUrl.Length -gt 500) { $refUrl = $refUrl.Substring(0, 500) }

        # CWE from CNA or ADP
        $cweId = $null
        $problemTypes = $cna.problemTypes
        if ($problemTypes) {
            foreach ($pt in $problemTypes) {
                foreach ($d in $pt.descriptions) {
                    if ($d.cweId) { $cweId = $d.cweId; break }
                }
                if ($cweId) { break }
            }
        }

        # CVSS from ADP (CISA enrichment) or CNA metrics
        $cvssScore = $null
        $severity = "medium"

        $adp = $cve.containers.adp
        if ($adp) {
            foreach ($a in $adp) {
                if ($a.metrics) {
                    foreach ($m in $a.metrics) {
                        if ($m.cvssV3_1) {
                            $cvssScore = [decimal]$m.cvssV3_1.baseScore
                            $severity = Get-SeverityFromScore $cvssScore
                            break
                        }
                    }
                }
                if ($cvssScore) { break }

                if (-not $cweId -and $a.problemTypes) {
                    foreach ($pt in $a.problemTypes) {
                        foreach ($d in $pt.descriptions) {
                            if ($d.cweId) { $cweId = $d.cweId; break }
                        }
                        if ($cweId) { break }
                    }
                }
            }
        }

        if (-not $cvssScore -and $cna.metrics) {
            foreach ($m in $cna.metrics) {
                if ($m.cvssV3_1) {
                    $cvssScore = [decimal]$m.cvssV3_1.baseScore
                    $severity = Get-SeverityFromScore $cvssScore
                    break
                }
                if ($m.cvssV3_0) {
                    $cvssScore = [decimal]$m.cvssV3_0.baseScore
                    $severity = Get-SeverityFromScore $cvssScore
                    break
                }
            }
        }

        return @{
            CveId          = $cveId
            Vendor         = $vendor
            Product        = $product
            ProductClass   = $productClass
            ProductPattern = $productPattern
            Severity       = $severity
            CvssScore      = $cvssScore
            Description    = $desc
            CweId          = $cweId
            PublishedAt    = $published
            AffectedBelow  = $affectedBelow
            FixedVersion   = $affectedBelow
            ReferencesUrl  = $refUrl
            CpeMatchString = $null
        }
    }
    catch {
        if ($script:parseErrors -lt 5) {
            Write-Host "PARSE-ERR [$jsonPath]: $($_.Exception.GetType().Name): $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "  LINE: $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim())" -ForegroundColor Red
        }
        $script:parseErrors++
        return $null
    }
}

function Execute-Record($record) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $mergeSql
    $cmd.CommandTimeout = 30

    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@cveId", [System.Data.SqlDbType]::NVarChar, 20))).Value = $record.CveId
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@vendor", [System.Data.SqlDbType]::NVarChar, 100))).Value = if ($record.Vendor) { $record.Vendor } else { [DBNull]::Value }
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@product", [System.Data.SqlDbType]::NVarChar, 256))).Value = if ($record.Product) { $record.Product } else { [DBNull]::Value }
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@productClass", [System.Data.SqlDbType]::NVarChar, 20))).Value = if ($record.ProductClass) { $record.ProductClass } else { [DBNull]::Value }
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@productPattern", [System.Data.SqlDbType]::NVarChar, 200))).Value = if ($record.ProductPattern) { $record.ProductPattern } else { [DBNull]::Value }
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@severity", [System.Data.SqlDbType]::NVarChar, 20))).Value = $record.Severity
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@cvssScore", [System.Data.SqlDbType]::Decimal))).Value = if ($null -ne $record.CvssScore) { $record.CvssScore } else { [DBNull]::Value }
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@description", [System.Data.SqlDbType]::NVarChar, -1))).Value = if ($record.Description) { $record.Description } else { [DBNull]::Value }
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@cweId", [System.Data.SqlDbType]::NVarChar, 20))).Value = if ($record.CweId) { $record.CweId } else { [DBNull]::Value }
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@publishedAt", [System.Data.SqlDbType]::DateTime2))).Value = if ($record.PublishedAt) { [DateTime]::Parse($record.PublishedAt) } else { [DBNull]::Value }
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@cpeMatchString", [System.Data.SqlDbType]::NVarChar, 500))).Value = [DBNull]::Value
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@affectedBelow", [System.Data.SqlDbType]::NVarChar, 100))).Value = if ($record.AffectedBelow) { $record.AffectedBelow } else { [DBNull]::Value }
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@fixedVersion", [System.Data.SqlDbType]::NVarChar, 100))).Value = if ($record.FixedVersion) { $record.FixedVersion } else { [DBNull]::Value }
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@referencesUrl", [System.Data.SqlDbType]::NVarChar, 500))).Value = if ($record.ReferencesUrl) { $record.ReferencesUrl } else { [DBNull]::Value }

    $cmd.ExecuteNonQuery() | Out-Null
    $cmd.Dispose()
}

# ── Main ──
$cvesRoot = Join-Path -Path $CvePath -ChildPath "cves"
if (-not (Test-Path -Path $cvesRoot)) {
    Write-Log "CVE root not found: $cvesRoot" -Level "ERROR"
    exit 1
}

$yearDirs = Get-ChildItem -Path $cvesRoot -Directory | Where-Object { $_.Name -match '^\d{4}$' } | Sort-Object Name
Write-Log "Found $($yearDirs.Count) year directories"

$totalProcessed = 0
$totalInserted = 0
$totalUpdated = 0
$totalSkipped = 0
$totalErrors = 0
$script:parseErrors = 0
$startTime = Get-Date

foreach ($yearDir in $yearDirs) {
    $yearStart = Get-Date
    $yearCount = 0

    $subDirs = Get-ChildItem -Path $yearDir.FullName -Directory | Sort-Object Name
    foreach ($subDir in $subDirs) {
        $files = Get-ChildItem -Path $subDir.FullName -Filter "CVE-*.json" -File
        foreach ($file in $files) {
            $totalProcessed++
            $record = Parse-CveRecord -jsonPath $file.FullName

            if (-not $record) {
                $totalSkipped++
                continue
            }

            try {
                Execute-Record $record
                $yearCount++
            }
            catch {
                $totalErrors++
                if ($totalErrors -le 10) {
                    Write-Log "Error on $($record.CveId): $($_.Exception.Message)" -Level "WARN"
                }
            }

            if ($totalProcessed % 5000 -eq 0) {
                $elapsed = (Get-Date) - $startTime
                $rate = [math]::Round($totalProcessed / $elapsed.TotalSeconds, 0)
                Write-Log "Progress: $totalProcessed processed, $totalErrors errors, $rate/sec"
            }
        }
    }

    $yearElapsed = ((Get-Date) - $yearStart).TotalSeconds
    Write-Log "Year $($yearDir.Name): $yearCount records in $([math]::Round($yearElapsed, 1))s"
}

$conn.Close()

$elapsed = ((Get-Date) - $startTime).TotalMinutes
Write-Log "=== IMPORT COMPLETE ==="
Write-Log "  Total files:    $totalProcessed"
Write-Log "  Skipped:        $totalSkipped (rejected/withdrawn)"
Write-Log "  Parse errors:   $($script:parseErrors)"
Write-Log "  DB errors:      $totalErrors"
Write-Log "  Time:           $([math]::Round($elapsed, 1)) minutes"
Write-Log "=== END ==="

exit 0
