<#
.SYNOPSIS
    Simulates the full Kryoss agent flow against a local or deployed API.

.DESCRIPTION
    Steps: 1) Enroll  2) Get Controls  3) Submit Results
    Uses HMAC-SHA256 signing for authenticated endpoints.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-06

.EXAMPLE
    .\Test-AgentFlow.ps1 -BaseUrl "https://localhost:7071/v1"
    .\Test-AgentFlow.ps1 -BaseUrl "https://kryoss-api.azurewebsites.net/v1" -EnrollmentCode "K7X9-M2P4-Q8R1-T5W3"
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "https://localhost:7071/v1",
    [string]$EnrollmentCode = "K7X9-M2P4-Q8R1-T5W3",
    [switch]$SkipEnroll
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ─── Helpers ────────────────────────────────────────────────────────
function Get-HmacSignature {
    param(
        [string]$Secret,
        [string]$Method,
        [string]$Path,
        [string]$Body = ""
    )
    $timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
    $bodyHash  = ([System.Security.Cryptography.SHA256]::Create().ComputeHash($bodyBytes) |
                  ForEach-Object { $_.ToString("x2") }) -join ''

    $signingString = "$timestamp$($Method.ToUpper())$Path$bodyHash"
    $keyBytes = [System.Text.Encoding]::UTF8.GetBytes($Secret)
    $msgBytes = [System.Text.Encoding]::UTF8.GetBytes($signingString)
    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = $keyBytes
    $signature = ($hmac.ComputeHash($msgBytes) | ForEach-Object { $_.ToString("x2") }) -join ''

    return @{
        Timestamp = $timestamp
        Signature = $signature
    }
}

# ─── Step 1: Enroll ────────────────────────────────────────────────
$apiKey    = $null
$apiSecret = $null
$agentId   = $null
$assessmentId = $null

if (-not $SkipEnroll) {
    Write-Host "`n══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  STEP 1: POST /v1/enroll" -ForegroundColor Cyan
    Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan

    $enrollBody = Get-Content "$scriptDir\01_enroll_request.json" -Raw
    $enrollBody = $enrollBody -replace '"K7X9-M2P4-Q8R1-T5W3"', "`"$EnrollmentCode`""

    Write-Host "  Request body:" -ForegroundColor Gray
    Write-Host $enrollBody

    try {
        $enrollResponse = Invoke-RestMethod -Uri "$BaseUrl/enroll" `
            -Method POST `
            -ContentType "application/json" `
            -Body $enrollBody

        Write-Host "`n  Response:" -ForegroundColor Green
        $enrollResponse | ConvertTo-Json -Depth 5 | Write-Host

        $apiKey       = $enrollResponse.apiKey
        $apiSecret    = $enrollResponse.apiSecret
        $agentId      = $enrollResponse.agentId
        $assessmentId = $enrollResponse.assessmentId
    }
    catch {
        Write-Host "  ENROLL FAILED: $_" -ForegroundColor Red
        Write-Host "  Using sample values for remaining steps..." -ForegroundColor Yellow

        # Fall back to sample data for demo purposes
        $sampleEnroll = Get-Content "$scriptDir\02_enroll_response.json" -Raw | ConvertFrom-Json
        $apiKey       = $sampleEnroll.apiKey
        $apiSecret    = $sampleEnroll.apiSecret
        $agentId      = $sampleEnroll.agentId
        $assessmentId = $sampleEnroll.assessmentId
    }
}
else {
    Write-Host "`n  Skipping enrollment (--SkipEnroll)" -ForegroundColor Yellow
    $sampleEnroll = Get-Content "$scriptDir\02_enroll_response.json" -Raw | ConvertFrom-Json
    $apiKey       = $sampleEnroll.apiKey
    $apiSecret    = $sampleEnroll.apiSecret
    $agentId      = $sampleEnroll.agentId
    $assessmentId = $sampleEnroll.assessmentId
}

Write-Host "`n  Agent ID:      $agentId" -ForegroundColor White
Write-Host "  API Key:       $($apiKey.Substring(0,12))..." -ForegroundColor White
Write-Host "  Assessment ID: $assessmentId" -ForegroundColor White

# ─── Step 2: Get Controls ──────────────────────────────────────────
Write-Host "`n══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  STEP 2: GET /v1/controls?assessmentId=$assessmentId" -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan

$controlsPath = "/v1/controls?assessmentId=$assessmentId"
$hmac = Get-HmacSignature -Secret $apiSecret -Method "GET" -Path $controlsPath

$headers = @{
    "X-Api-Key"   = $apiKey
    "X-Timestamp" = $hmac.Timestamp
    "X-Signature" = $hmac.Signature
}

Write-Host "  Headers:" -ForegroundColor Gray
Write-Host "    X-Api-Key:   $($apiKey.Substring(0,12))..."
Write-Host "    X-Timestamp: $($hmac.Timestamp)"
Write-Host "    X-Signature: $($hmac.Signature.Substring(0,16))..."

try {
    $controlsResponse = Invoke-RestMethod -Uri "$BaseUrl/controls?assessmentId=$assessmentId" `
        -Method GET `
        -Headers $headers

    $checkCount = $controlsResponse.checks.Count
    Write-Host "`n  Got $checkCount controls (version: $($controlsResponse.version))" -ForegroundColor Green
    Write-Host "  Types: $(($controlsResponse.checks | Group-Object type | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', ')" -ForegroundColor White
}
catch {
    Write-Host "  GET CONTROLS FAILED: $_" -ForegroundColor Red
    Write-Host "  Loading from sample file..." -ForegroundColor Yellow
    $controlsResponse = Get-Content "$scriptDir\03_controls_response.json" -Raw | ConvertFrom-Json
    $checkCount = $controlsResponse.checks.Count
    Write-Host "  Loaded $checkCount controls from sample" -ForegroundColor Yellow
}

# ─── Step 3: Submit Results ────────────────────────────────────────
Write-Host "`n══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  STEP 3: POST /v1/results" -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan

$resultsBody = Get-Content "$scriptDir\04_results_request.json" -Raw
# Replace agentId in sample with actual
$resultsBody = $resultsBody -replace '"a1b2c3d4-e5f6-7890-abcd-ef1234567890"', "`"$agentId`""

$resultsPath = "/v1/results"
$hmac = Get-HmacSignature -Secret $apiSecret -Method "POST" -Path $resultsPath -Body $resultsBody

$headers = @{
    "X-Api-Key"   = $apiKey
    "X-Timestamp" = $hmac.Timestamp
    "X-Signature" = $hmac.Signature
}

$resultCount = ($resultsBody | ConvertFrom-Json).results.Count
Write-Host "  Submitting $resultCount check results..." -ForegroundColor Gray

# Debug: show what the client is signing
$debugBodyBytes = [System.Text.Encoding]::UTF8.GetBytes($resultsBody)
$debugBodyHash  = ([System.Security.Cryptography.SHA256]::Create().ComputeHash($debugBodyBytes) |
                   ForEach-Object { $_.ToString("x2") }) -join ''
$debugSignStr   = "$($hmac.Timestamp)POST$resultsPath$debugBodyHash"
Write-Host "  HMAC Debug:" -ForegroundColor Yellow
Write-Host "    Path:       $resultsPath"
Write-Host "    BodyLength: $($debugBodyBytes.Length) bytes"
Write-Host "    BodyHash:   $debugBodyHash"
Write-Host "    SignString: $($debugSignStr.Substring(0, [Math]::Min(120, $debugSignStr.Length)))..."
Write-Host "  Headers:" -ForegroundColor Gray
Write-Host "    X-Api-Key:   $($apiKey.Substring(0,12))..."
Write-Host "    X-Timestamp: $($hmac.Timestamp)"
Write-Host "    X-Signature: $($hmac.Signature.Substring(0,16))..."

try {
    $resultsResponse = Invoke-RestMethod -Uri "$BaseUrl/results" `
        -Method POST `
        -ContentType "application/json" `
        -Headers $headers `
        -Body $resultsBody

    Write-Host "`n  Response:" -ForegroundColor Green
    $resultsResponse | ConvertTo-Json -Depth 5 | Write-Host
}
catch {
    Write-Host "  SUBMIT RESULTS FAILED: $_" -ForegroundColor Red
    Write-Host "`n  Expected response would be:" -ForegroundColor Yellow
    Get-Content "$scriptDir\05_results_response.json" -Raw | Write-Host
}

# ─── Summary ───────────────────────────────────────────────────────
Write-Host "`n══════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  TEST COMPLETE" -ForegroundColor Green
Write-Host "══════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  Enroll:   POST /v1/enroll"
Write-Host "  Controls: GET  /v1/controls?assessmentId=$assessmentId ($checkCount checks)"
Write-Host "  Results:  POST /v1/results ($resultCount results)"
Write-Host ""
