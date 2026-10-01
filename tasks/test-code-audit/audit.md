# Practical audit fixture and reproduction

Package 7 was audited on implementation commit `99b2853f79e520e5469d27212986dfa5eebe6e5e`, with all eight production analyses enabled and their repository defaults unchanged. This is an explicit **FULL AUDIT** of an isolated input solution; it is not an audit of the AiNetReview product's own findings. The source assessments, initial run evidence, and defect-resolution record are documented directly under package 7 in [the roadmap](roadmap.md).

## Correction verification

The original flat input was rerun after correcting structural-fragment resource growth and report-member navigation. The source fixture remained unchanged: 1005 executable assertions, 3105 raw-string data records, the same production and test projects, and the same eight enabled analyses with their defaults. The new FULL AUDIT root is `temp/practical-audit-7/fixture/reports/20261001T101955Z-d36d4612/index.md`. It completed with exit code 0 in **12.573 s**. A monitor refreshed the host process every 200 ms before reading `PrivateMemorySize64`; the highest sampled host value was **74.5 MiB**. The run used a 1.5-GiB sampled-memory stop and a 180-second time limit. This sampled value is not the Windows lifetime peak counter used by the separate release gate.

The complete views remain **production 2 / tests 9 / mixed 1 = 12 findings**, with the same seven present signal types and the same subjects as the previous source assessment. In the flat input, the principal test locations are `ScenarioTests.cs:6` (`LongScenario`, 1009 reported code lines and 1005 identical assertions), `StringFixtureTests.cs:1` (3118 physical lines, 3105 declarative records), `ScenarioTests.cs:1015` (`ConditionalScenario`), `:1033` / `:1043` (`DuplicateScenarioA/B`), `:1053` (`Märchen`), `:1058` (`ObsoleteHelper`), and `:1059` (`ExpectedScore`). The mixed duplicate still links `Product/Calculator.cs:4-12` with `ExpectedScore`; the production signals still refer to `Calculator.UntestedCategory` at lines 13-25. The existing source classifications therefore remain applicable. Structural duplication remains an active analysis with zero findings in this fixture; no absence-of-signal quality claim is inferred.

All generated report navigation was checked after publication: **89 relative links across 25 Markdown files resolve**, including cross-area links to the non-representative production cluster member and to all three forwarding files. Member links retain their line coordinates and production/test roles; the writer regression covers URL-encoded `#`, braces, spaces, brackets, and non-ASCII characters. The existing tests-only assessment remains limited to its nine test findings and explicitly leaves production (2) and mixed (1) unreviewed; the separate FULL AUDIT above covers all three areas.

The cross-owner regression uses two flat owners with 100 identical statements. It produces the complete maximal 100-statement group with both occurrences and completes in **7 s** in the test host. A guarded process-tree run of the FastTest command took **10.285 s** and sampled **455.2 MiB** total private memory across descendants every 300 ms, below its 2-GiB / 180-second stops. This bounded case checks a real duplicate path without claiming performance for two 1005-statement owners; that larger case was not run. Fingerprints only preselect candidates; complete ordinal-normalized strings remain the grouping authority, so collisions are resolved by exact comparison.

The following generator is an audit input resource, not production or test-suite code. It creates two .NET 10 projects, a real metadata-backed xUnit v3 reference (3.2.2), 1005 executable assertions, 3105 raw-string data records, active and skipped test repetition, copied expectation logic, and a three-file test helper chain. It does not run tests or refactor inputs. `-Grouped` only adds explicit nested source blocks; it retains every assertion and data record and does not change analysis settings. No baseline is created. The original flat variant is retained as the resource-growth reproduction and must not be replaced by the grouped variant when verifying that defect.

Save the fenced script below as `temp/practical-audit-7-repro/create.ps1` from the repository root, then run `pwsh -NoProfile -File temp/practical-audit-7-repro/create.ps1` for the original flat variant, or append `-Grouped` for the additional nested variant. Use a fresh directory name if it already contains fixture inputs; the script intentionally preserves existing runs. The original resource-growth reproduction was manually terminated after 117.506 seconds at 11933 MiB observed private memory; monitor its host process and terminate it if needed. The grouped input is suitable for independently checking report navigation and all commissioned source assessments. The Debug host must have been built before either invocation.

```powershell
param([switch]$Grouped)
$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path $PSScriptRoot $(if ($Grouped) { 'fixture-grouped-nested' } else { 'fixture' })
if (Test-Path $fixtureRoot) { throw 'Use a fresh fixture directory; existing reports are preserved.' }
New-Item -ItemType Directory -Path "$fixtureRoot/Product", "$fixtureRoot/Example.Tests" -Force | Out-Null
$utf8 = [System.Text.UTF8Encoding]::new($false)
function Write-Input($path, $content) { [IO.File]::WriteAllText((Join-Path $fixtureRoot $path), $content.Replace("`r`n", "`n"), $utf8) }
Write-Input 'Directory.Build.props' '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableNETAnalyzers>false</EnableNETAnalyzers><TreatWarningsAsErrors>false</TreatWarningsAsErrors><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>'
Write-Input 'Directory.Packages.props' '<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>'
Write-Input 'Fixture.slnx' '<Solution><Project Path="Product/Product.csproj" /><Project Path="Example.Tests/Example.Tests.csproj" /></Solution>'
Write-Input 'Product/Product.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'
Write-Input 'Example.Tests/Example.Tests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Product/Product.csproj" /><PackageReference Include="xunit.v3.extensibility.core" Version="3.2.2" /></ItemGroup></Project>'
$scoreBody = @'
    {
        int score = value * 2;
        score += value > 0 ? 2 : 0;
        score += value > 5 ? 3 : 0;
        score += value > 10 ? 4 : 0;
        score += value > 20 ? 5 : 0;
        return score;
    }
'@
Write-Input 'Product/Calculator.cs' (@'
namespace AuditProduct;
public static class Calculator
{
    public static int Score(int value)
'@ + "`n" + $scoreBody + @'

    public static int UntestedCategory(int value)
    {
        if (value < 0) return -1;
        if (value == 0) return 0;
        if (value == 1) return 1;
        if (value == 2) return 2;
        if (value == 3) return 3;
        if (value == 4) return 4;
        if (value == 5) return 5;
        if (value == 6) return 6;
        if (value == 7) return 7;
        return 8;
    }
}
'@)
$longAssertions = ((1..1005 | ForEach-Object {
    if ($Grouped -and ($_ % 50 -eq 1)) { '        {' }
    if ($Grouped -and ($_ % 5 -eq 1)) { '        {' }
    '        Check(Calculator.Score(1), 4);'
    if ($Grouped -and ($_ % 5 -eq 0)) { '        }' }
    if ($Grouped -and (($_ % 50 -eq 0) -or ($_ -eq 1005))) { '        }' }
}) -join "`n")
Write-Input 'Example.Tests/ScenarioTests.cs' (@'
using AuditProduct;
using Xunit;
namespace AuditTests;
public sealed class ScenarioTests
{
    [Fact]
    public void LongScenario()
    {
'@ + "`n" + $longAssertions + @'

    }
    [Fact]
    public void ConditionalScenario()
    {
        for (int value = -2; value < 25; value++)
        {
            if (value >= 0)
            {
                if (value % 2 == 0)
                {
                    if (value > 5)
                    {
                        if (value > 10) Check(Calculator.Score(value), ExpectedScore(value));
                    }
                }
            }
        }
    }
    [Fact]
    public void DuplicateScenarioA()
    {
        int actual = Calculator.Score(1);
        int expected = 4;
        if (actual != expected) throw new System.InvalidOperationException("score mismatch");
        actual = Calculator.Score(6);
        expected = 17;
        if (actual != expected) throw new System.InvalidOperationException("score mismatch");
    }
    [Fact(Skip = "Illustrative disabled regression")]
    public void DuplicateScenarioB()
    {
        int actual = Calculator.Score(1);
        int expected = 4;
        if (actual != expected) throw new System.InvalidOperationException("score mismatch");
        actual = Calculator.Score(6);
        expected = 17;
        if (actual != expected) throw new System.InvalidOperationException("score mismatch");
    }
    [Fact]
    public void Märchen() => Check(Bridge.Score(1), 4);
    private static void Check(int actual, int expected)
    {
        if (actual != expected) throw new System.InvalidOperationException("score mismatch");
    }
    private static int ObsoleteHelper(int value) => value + 99;
    private static int ExpectedScore(int value)
'@ + "`n" + $scoreBody + "`n}`n")
foreach ($layer in @(@('Bridge','Adapter'), @('Adapter','Endpoint'))) {
    Write-Input ("Example.Tests/" + $layer[0] + '.cs') ("namespace AuditTests;`npublic static class " + $layer[0] + "`n{`n    public static int Score(int value) => " + $layer[1] + ".Score(value);`n}`n")
}
Write-Input 'Example.Tests/Endpoint.cs' "using AuditProduct;`nnamespace AuditTests;`npublic static class Endpoint`n{`n    public static int Score(int value) => Calculator.Score(value);`n}`n"
$fixtureLines = ((1..3105 | ForEach-Object { "record-$_=declarative payload" }) -join "`n")
Write-Input 'Example.Tests/StringFixtureTests.cs' (@'
using Xunit;
namespace AuditTests;
public sealed class StringFixtureTests
{
    private const string Payload = """
'@ + "`n" + $fixtureLines + @'

""";
    [Fact]
    public void FixtureIsPresent()
    {
        if (!Payload.Contains("record-3105=declarative payload", System.StringComparison.Ordinal))
            throw new System.InvalidOperationException("fixture is incomplete");
    }
}
'@)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$config = Get-Content (Join-Path $repoRoot 'ainetreview.json') -Raw | ConvertFrom-Json
$config.solution = 'Fixture.slnx'
$config.outputDirectory = 'reports'
Write-Input 'ainetreview.json' ($config | ConvertTo-Json -Depth 10)
dotnet restore "$fixtureRoot/Fixture.slnx" --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Fixture restore failed.' }
$hostExe = Join-Path $repoRoot 'src/AiNetReview/bin/Debug/net10.0/AiNetReview.exe'
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
& $hostExe review $fixtureRoot
$auditExit = $LASTEXITCODE
$stopwatch.Stop()
"ElapsedSeconds=$($stopwatch.Elapsed.TotalSeconds.ToString('F3', [Globalization.CultureInfo]::InvariantCulture)); ExitCode=$auditExit"
if ($auditExit -ne 0) { throw 'Audit failed.' }

```

The existing local originals are under `temp/practical-audit-7/`: `fixture/` (flat input, interrupted), `fixture-grouped/` (single level of five-assertion blocks, completed), and `fixture-grouped-nested/` (the reproducible `-Grouped` variant above, completed). The completed nested run's shared root is `fixture-grouped-nested/reports/20261001T095840Z-766ab58e/index.md`. Start there and use its three `all-findings` links only with the explicit full-audit assignment. These input and output directories are Git-ignored and are not durable repository documentation of current product behavior.
