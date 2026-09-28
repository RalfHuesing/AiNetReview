#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,63}$')]
    [string]$Target = 'ainetreview'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$profilePath = Join-Path $repoRoot "audit-targets/$Target.json"
if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
    throw "Audit profile '$profilePath' does not exist. Add audit-targets/<name>.json first."
}

$tempDir = Join-Path $repoRoot 'temp'
$resultsDir = Join-Path $repoRoot 'TestResults'
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null

$logFile = Join-Path $tempDir "test-audit-$Target.log"
$trxFile = "Audit-$Target.trx"
$projectPath = Join-Path $repoRoot 'tests/AiNetReview.IntegrationTests/AiNetReview.IntegrationTests.csproj'
$previousTarget = [Environment]::GetEnvironmentVariable('AINETREVIEW_AUDIT_TARGET', 'Process')

try {
    [Environment]::SetEnvironmentVariable('AINETREVIEW_AUDIT_TARGET', $Target, 'Process')
    Write-Host "[INFO] Starte manuelles Audit für '$Target'." -ForegroundColor Cyan
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
}

exit $exitCode
