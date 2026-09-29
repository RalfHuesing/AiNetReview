#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,63}$')]
    [string]$Target = 'ainetreview',

    [Parameter()]
    [switch]$BaselineOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$profilePath = Join-Path $repoRoot "audit-targets/$Target.json"
if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
    throw "Audit profile '$profilePath' does not exist. Add audit-targets/<name>.json first."
}

$profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json -AsHashtable
if ($profile -isnot [System.Collections.IDictionary]) {
    throw "Audit profile '$profilePath' must contain a JSON object."
}
if ($profile.Contains('enabled')) {
    if ($profile['enabled'] -isnot [bool]) {
        throw "Audit profile field 'enabled' must be a boolean."
    }
    if (-not $profile['enabled']) {
        Write-Host "[INFO] Audit für '$Target' ist im Profil deaktiviert."
        exit 0
    }
}

$tempDir = Join-Path $repoRoot 'temp'
$resultsDir = Join-Path $repoRoot 'TestResults'
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null

$logFile = Join-Path $tempDir "test-audit-$Target.log"
$trxFile = "Audit-$Target.trx"
$projectPath = Join-Path $repoRoot 'tests/AiNetReview.IntegrationTests/AiNetReview.IntegrationTests.csproj'
$previousTarget = [Environment]::GetEnvironmentVariable('AINETREVIEW_AUDIT_TARGET', 'Process')
$previousBaselineOnly = [Environment]::GetEnvironmentVariable('AINETREVIEW_AUDIT_BASELINE_ONLY', 'Process')

try {
    [Environment]::SetEnvironmentVariable('AINETREVIEW_AUDIT_TARGET', $Target, 'Process')
    [Environment]::SetEnvironmentVariable('AINETREVIEW_AUDIT_BASELINE_ONLY', $(if ($BaselineOnly) { '1' } else { $null }), 'Process')
    $action = if ($BaselineOnly) { 'aktualisiere zentrale Baseline für' } else { 'starte manuelles Audit für' }
    Write-Host "[INFO] $action '$Target'." -ForegroundColor Cyan
    Write-Host "[INFO] Profil: $profilePath" -ForegroundColor DarkGray
    Write-Host "[INFO] Ergebnis: $(Join-Path $repoRoot "audit-reporting/$Target")" -ForegroundColor DarkGray
    Write-Host "[INFO] Log: $logFile" -ForegroundColor DarkGray

    & dotnet test $projectPath --filter 'Category=Audit' --logger "trx;LogFileName=$trxFile" --results-directory $resultsDir 2>&1 |
        Tee-Object -FilePath $logFile
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Write-Host "[ERROR] Audit fehlgeschlagen mit Exit-Code $exitCode. Log: $logFile" -ForegroundColor Red
    } else {
        Write-Host "[INFO] Audit abgeschlossen. Log: $logFile" -ForegroundColor Green
    }
} finally {
    [Environment]::SetEnvironmentVariable('AINETREVIEW_AUDIT_TARGET', $previousTarget, 'Process')
    [Environment]::SetEnvironmentVariable('AINETREVIEW_AUDIT_BASELINE_ONLY', $previousBaselineOnly, 'Process')
}

exit $exitCode
