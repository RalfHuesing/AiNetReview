#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resultsDir = Join-Path $repoRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
$projectPath = Join-Path $repoRoot 'tests/AiNetReview.IntegrationTests/AiNetReview.IntegrationTests.csproj'
$testArgs = @(
    'test',
    $projectPath,
    '--filter', 'Category=Performance',
    '--logger', 'trx;LogFileName=PerformanceTests.trx',
    '--results-directory', $resultsDir
)
if ($AdditionalArgs) {
    $testArgs += $AdditionalArgs
}

& dotnet @testArgs
exit $LASTEXITCODE
