#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and runs the Lantern.Feasibility tool.

.DESCRIPTION
    Optionally generates sample documents, then runs Meilisearch connectivity
    and document extraction feasibility tests.

.PARAMETER Args
    Arguments forwarded to Lantern.Feasibility.
    Examples:
        --meili                 Meilisearch test only
        --extract <path>        Single-file extraction test
        --generate-samples      Generate sample documents only
        (none)                  Run all tests

.EXAMPLE
    .\scripts\run-feasibility.ps1

.EXAMPLE
    $env:MEILI_MASTER_KEY = "your-dev-key"
    .\scripts\run-feasibility.ps1 -- --meili

.NOTES
    MEILI_MASTER_KEY must be set for Meilisearch tests.
    Meilisearch must be running (use start-meilisearch.ps1).
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ToolArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$repoRoot = Split-Path $scriptDir -Parent
$feasibilityProject = Join-Path $repoRoot "tools\Lantern.Feasibility\Lantern.Feasibility.csproj"

if (-not (Test-Path $feasibilityProject)) {
    Write-Error "Feasibility project not found at: $feasibilityProject"
    exit 1
}

Write-Host "Building Lantern.Feasibility..."
dotnet build $feasibilityProject -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit $LASTEXITCODE
}

Write-Host "Running Lantern.Feasibility..."
Write-Host ""

if ($ToolArgs.Count -gt 0) {
    dotnet run --project $feasibilityProject -c Release --no-build -- @ToolArgs
} else {
    dotnet run --project $feasibilityProject -c Release --no-build
}

exit $LASTEXITCODE
