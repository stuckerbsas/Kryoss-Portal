<#
.SYNOPSIS
    Deletes ALL Kryoss Azure resources (nuclear option).

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-06

.EXAMPLE
    .\Remove-Azure.ps1
    .\Remove-Azure.ps1 -ResourceGroup "rg-kryoss-dev"
#>
[CmdletBinding()]
param(
    [string]$ResourceGroup = "rg-kryoss"
)

Write-Host ""
Write-Host "  ⚠  THIS WILL DELETE EVERYTHING IN: $ResourceGroup" -ForegroundColor Red
Write-Host "     Including: SQL Server, Database, Functions App, Storage" -ForegroundColor Red
Write-Host ""

$confirm = Read-Host "  Type 'DELETE' to confirm"
if ($confirm -ne 'DELETE') {
    Write-Host "  Cancelled." -ForegroundColor Yellow
    return
}

Write-Host ""
Write-Host "  Deleting resource group '$ResourceGroup'..." -ForegroundColor Yellow
az group delete --name $ResourceGroup --yes --no-wait
Write-Host "  Deletion initiated (runs in background, ~2-5 minutes)." -ForegroundColor Green
Write-Host "  Verify with: az group show --name $ResourceGroup" -ForegroundColor Gray
Write-Host ""
