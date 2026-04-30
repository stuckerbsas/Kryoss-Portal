<#
.SYNOPSIS
    Bulk-enrich CVSS scores from NVD API 2.0 for all cve_entries missing cvss_score.

.DESCRIPTION
    Downloads ALL NVD CVE data page-by-page (2000/page), matches against our cve_entries
    by cve_id, and updates cvss_score + severity. Much faster than per-CVE queries.

    Use -LocalFile to skip download and load from a previously saved JSON file.
    After each download, data is auto-saved to nvd-cvss-cache.json for reuse.

.PARAMETER NvdApiKey
    NVD API key. Required unless -LocalFile is provided.

.PARAMETER LocalFile
    Path to a previously saved JSON file with CVSS data. Skips NVD download entirely.

.PARAMETER BatchSize
    SQL UPDATE batch size. Default 100.

.NOTES
    Author:   TeamLogic IT
    Version:  1.1
    Created:  2026-04-30

.EXAMPLE
    .\Import-CvssScores.ps1 -NvdApiKey "your-key-here"
.EXAMPLE
    .\Import-CvssScores.ps1 -LocalFile ".\nvd-cvss-cache.json"
#>

[CmdletBinding()]
param(
    [string]$NvdApiKey,

    [string]$LocalFile,

    [int]$BatchSize = 100
)

if (-not $LocalFile -and -not $NvdApiKey) {
    Write-Error "Provide -NvdApiKey for download or -LocalFile to load from cache."
    exit 1
}

$ErrorActionPreference = 'Stop'

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "[$timestamp] [$Level] $Message"
}

# ── Connect to Azure SQL ──
Write-Log "Acquiring Azure SQL access token..."
$accessToken = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv 2>$null
if (-not $accessToken) {
    Write-Log "Failed to get Azure SQL token. Run 'az login' first." -Level "ERROR"
    exit 1
}

$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = "Server=tcp:sql-kryoss.database.windows.net,1433;Database=KryossDb;Encrypt=True;TrustServerCertificate=False;"
$conn.AccessToken = $accessToken
$conn.Open()
Write-Log "Connected to Azure SQL"

# ── Load our CVE IDs that need CVSS ──
Write-Log "Loading CVEs missing CVSS scores..."
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT cve_id FROM cve_entries WHERE cvss_score IS NULL"
$cmd.CommandTimeout = 120
$reader = $cmd.ExecuteReader()
$missingCves = @{}
while ($reader.Read()) {
    $missingCves[$reader.GetString(0)] = $true
}
$reader.Close()
Write-Log "Found $($missingCves.Count) CVEs missing CVSS scores"

if ($missingCves.Count -eq 0) {
    Write-Log "Nothing to enrich"
    $conn.Close()
    exit 0
}

# ── Load enrichments: from local file or NVD API ──
$enrichments = @{}

if ($LocalFile) {
    Write-Log "Loading CVSS data from local file: $LocalFile"
    $jsonData = Get-Content -Path $LocalFile -Raw | ConvertFrom-Json
    foreach ($prop in $jsonData.PSObject.Properties) {
        if ($missingCves.ContainsKey($prop.Name)) {
            $enrichments[$prop.Name] = @{ Score = [decimal]$prop.Value.Score; Severity = $prop.Value.Severity }
        }
    }
    Write-Log "Loaded $($enrichments.Count) matching CVEs from cache (file has $($jsonData.PSObject.Properties.Count) total)"
}
else {
    $nvdBase = "https://services.nvd.nist.gov/rest/json/cves/2.0/"
    $pageSize = 2000
    $startIndex = 0
    $totalResults = $null
    $allNvdScores = @{}
    $pageNum = 0

    Write-Log "Starting NVD bulk download (pageSize=$pageSize)..."

    do {
        $pageNum++
        $url = "${nvdBase}?resultsPerPage=${pageSize}&startIndex=${startIndex}"

        $headers = @{ "apiKey" = $NvdApiKey }

        $retries = 0
        $response = $null
        while ($retries -lt 3) {
            try {
                $response = Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 120
                break
            }
            catch {
                $retries++
                if ($retries -ge 3) {
                    Write-Log "Failed page $pageNum after 3 retries: $($_.Exception.Message)" "WARN"
                    break
                }
                Write-Log "Retry $retries for page $pageNum..." "WARN"
                Start-Sleep -Seconds 5
            }
        }

        if ($null -eq $response) {
            $startIndex += $pageSize
            continue
        }

        if ($null -eq $totalResults) {
            $totalResults = $response.totalResults
            $totalPages = [Math]::Ceiling($totalResults / $pageSize)
            Write-Log "NVD total: $totalResults CVEs ($totalPages pages)"
        }

        foreach ($vuln in $response.vulnerabilities) {
            $cveId = $vuln.cve.id
            $cvss31 = $vuln.cve.metrics.cvssMetricV31
            $cvss2 = $vuln.cve.metrics.cvssMetricV2

            $score = $null
            $severity = $null

            if ($cvss31 -and $cvss31.Count -gt 0) {
                $score = $cvss31[0].cvssData.baseScore
                $severity = $cvss31[0].cvssData.baseSeverity
            }
            elseif ($cvss2 -and $cvss2.Count -gt 0) {
                $score = $cvss2[0].cvssData.baseScore
                $severity = $cvss2[0].cvssData.baseSeverity
            }

            if ($null -ne $score) {
                $allNvdScores[$cveId] = @{ Score = $score; Severity = $severity }
            }
        }

        Write-Log "Page ${pageNum}/${totalPages}: startIndex=$startIndex, total scores=$($allNvdScores.Count)"
        $startIndex += $pageSize

        Start-Sleep -Milliseconds 650
    } while ($startIndex -lt $totalResults)

    # Save ALL scores to cache file for reuse
    $cachePath = Join-Path $PSScriptRoot "nvd-cvss-cache.json"
    $allNvdScores | ConvertTo-Json -Depth 3 -Compress | Set-Content -Path $cachePath -Encoding UTF8
    Write-Log "Saved $($allNvdScores.Count) CVSS scores to $cachePath"

    # Filter to only CVEs we need
    foreach ($kv in $allNvdScores.GetEnumerator()) {
        if ($missingCves.ContainsKey($kv.Key)) {
            $enrichments[$kv.Key] = $kv.Value
        }
    }

    Write-Log "NVD download complete. $($enrichments.Count) matching CVEs out of $($allNvdScores.Count) total"
}

# ── Update database ──
if ($enrichments.Count -eq 0) {
    Write-Log "No CVSS scores to update"
    $conn.Close()
    exit 0
}

Write-Log "Updating database..."
$updated = 0
$errors = 0

function New-UpdateCommand {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "UPDATE cve_entries SET cvss_score = @score, severity = @sev, updated_at = GETUTCDATE() WHERE cve_id = @id AND cvss_score IS NULL"
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@score", [System.Data.SqlDbType]::Decimal))) | Out-Null
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@sev", [System.Data.SqlDbType]::NVarChar, 20))) | Out-Null
    $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@id", [System.Data.SqlDbType]::VarChar, 20))) | Out-Null
    $cmd.CommandTimeout = 30
    return $cmd
}

function Reconnect-SqlConnection {
    Write-Log "Reconnecting to Azure SQL..." "WARN"
    try { $conn.Close() } catch {}
    $newToken = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv 2>$null
    if ($newToken) { $conn.AccessToken = $newToken }
    $conn.Open()
    Write-Log "Reconnected"
}

$updateCmd = New-UpdateCommand

foreach ($entry in $enrichments.GetEnumerator()) {
    try {
        if ($conn.State -ne [System.Data.ConnectionState]::Open) {
            Reconnect-SqlConnection
            $updateCmd = New-UpdateCommand
        }
        $updateCmd.Parameters["@id"].Value = $entry.Key
        $updateCmd.Parameters["@score"].Value = [decimal]$entry.Value.Score
        $updateCmd.Parameters["@sev"].Value = if ($entry.Value.Severity) { $entry.Value.Severity.ToLower() } else {
                $s = [decimal]$entry.Value.Score
                if ($s -ge 9.0) { 'critical' } elseif ($s -ge 7.0) { 'high' } elseif ($s -ge 4.0) { 'medium' } else { 'low' }
            }
        $updateCmd.ExecuteNonQuery() | Out-Null
        $updated++

        if ($updated % 1000 -eq 0) {
            Write-Log "Progress: $updated/$($enrichments.Count) updated"
        }
    }
    catch {
        if ($_.Exception.Message -match 'connection is broken|connection was closed') {
            try {
                Reconnect-SqlConnection
                $updateCmd = New-UpdateCommand
                $updateCmd.Parameters["@id"].Value = $entry.Key
                $updateCmd.Parameters["@score"].Value = [decimal]$entry.Value.Score
                $updateCmd.Parameters["@sev"].Value = if ($entry.Value.Severity) { $entry.Value.Severity.ToLower() } else {
                $s = [decimal]$entry.Value.Score
                if ($s -ge 9.0) { 'critical' } elseif ($s -ge 7.0) { 'high' } elseif ($s -ge 4.0) { 'medium' } else { 'low' }
            }
                $updateCmd.ExecuteNonQuery() | Out-Null
                $updated++
            }
            catch {
                $errors++
                if ($errors -le 5) { Write-Log "Error (retry) $($entry.Key): $($_.Exception.Message)" "WARN" }
            }
        }
        else {
            $errors++
            if ($errors -le 5) { Write-Log "Error updating $($entry.Key): $($_.Exception.Message)" "WARN" }
        }
    }
}

Write-Log "=== CVSS ENRICHMENT COMPLETE ==="
Write-Log "  CVEs missing CVSS:  $($missingCves.Count)"
Write-Log "  Found in NVD:       $($enrichments.Count)"
Write-Log "  Updated in DB:      $updated"
Write-Log "  Errors:             $errors"
Write-Log "  Not in NVD:         $($missingCves.Count - $enrichments.Count)"
Write-Log "=== END ==="

$conn.Close()
