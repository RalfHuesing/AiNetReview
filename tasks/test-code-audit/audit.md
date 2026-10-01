# Independent final audit (package 8)

Audited implementation: `f8858b24a60e330805eaf1aa7597a3189c850d75`, initially clean. The approved concept, roadmap/evidence, current documentation, configuration, changed production/test sources and generated reports were inspected independently. C# navigation began with AiNetLinter read-only symbol tools. No product or test-suite source was changed. Three isolated real-framework CLI inputs were created under ignored `temp/`; all completed successfully and demonstrate the defects below. Package 8 remains open; packages 4 and 6 are reopened because their complete framework-protection acceptance is not met. The workflow permits at most one correction implementer after this audit.

## Findings

### P1 — Real MSTest 4.x metadata loses all attribute protection

`src/AiNetReview.Core/ReviewAnalyses/DeadCodeCandidates/DeadCodeTestFrameworkUsageCollector.cs:675-679` accepts MSTest metadata only when the assembly name starts with `Microsoft.VisualStudio.TestPlatform.TestFramework`. Installed MSTest.TestFramework **4.3.3** uses `MSTest.TestFramework.dll`; the actual assembly identity has that new name. The [primary framework project](https://github.com/microsoft/testfx/blob/main/src/TestFramework/TestFramework/TestFramework.csproj) declares it, and the [AssemblyFixtureProvider API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.testtools.unittesting.assemblyfixtureproviderattribute) documents the same assembly. Consequently genuine TestClass/TestMethod, hooks, DynamicData, and modern AssemblyFixtureProvider attributes fail this identity gate. The emitted matrix at `tests/AiNetReview.FastTests/ReviewAnalyses/DeadCodeCandidatesAnalysisTests.cs:702` instead combines modern fixture-provider/global-hook APIs with the old assembly name, hiding the failure.

Reproduction: isolated .NET 10 solution `temp/final-audit-8-mstest/Repro.slnx`, project `Repro.Tests.csproj`, PackageReference `MSTest.TestFramework` pinned to `4.3.3`; dead-code enabled with `apiSurface: "closed_solution"`, all other analyses explicitly disabled. Ordinary SDK restore succeeded. Its complete input is:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
[TestClass]
public sealed class ValidTests
{
    [TestMethod] public void ValidCase() { }
    [TestInitialize] public void Setup() { }
    private void OrdinaryHelper() { }
}
```

Run from the repository root: `src/AiNetReview/bin/Debug/net10.0/AiNetReview.exe review temp/final-audit-8-mstest`. Actual exit 0, detected 1, run `20261001T103352Z-7ce77c61`: `tests/all-findings/dead-code-candidates.md` reports **T:ValidTests, Type without known use**. The root correctly classifies the project as tests. Expected under the approved protection contract: ValidTests and its test/hook are protected; only OrdinaryHelper remains a candidate. This is a grouped false unused finding over a genuine runner-bound test class, not a missing package or a compile failure. Correct identity recognition and verify real metadata from both old and new assembly generations. Affects concept criterion 5 and package 4, plus the complete-acceptance claim in package 6.

### P2 — Local provider uncertainty misses agreed plausible members

`DeadCodeTestFrameworkUsageCollector.cs:357-379` protects/marks only exact-name matches when there are multiple matches. In contrast, the concept's dead-code contract requires **all plausible provider members of the known source type** to be uncertain for ambiguous named sources. The existing ambiguity case at `DeadCodeCandidatesAnalysisTests.cs:772` checks private-helper candidacy but has no differently named plausible public static provider, so it cannot distinguish these behaviors.

Reproduction: isolated .NET 10 solution `temp/final-audit-8-xunit/Repro.slnx`, actual `xunit.v3.extensibility.core` **3.2.2**, same dead-code-only configuration and closed-solution API mode:

```csharp
using Xunit;
using System.Collections.Generic;
public sealed class Tests
{
    [Theory, MemberData("Rows")] public void Case(int value) { }
    public static IEnumerable<object[]> Rows() => [];
    public static IEnumerable<object[]> Rows(int count) => [];
    public static IEnumerable<object[]> Alternative() => [];
    private void PrivateHelper() { }
}
```

Restore and `src/AiNetReview/bin/Debug/net10.0/AiNetReview.exe review temp/final-audit-8-xunit` both succeeded. Run `20261001T103532Z-696fe157` reports **Alternative and PrivateHelper** (detected 2). Alternative satisfies the collector's own plausible-provider predicate at `:551-553`; the two Rows overloads make the name ambiguous. The approved conservative contract selects only PrivateHelper here. Apply the same type-local plausible-provider uncertainty, including inherited providers, that is already used for unknown names; retain private-helper and unrelated-project candidacy. This finding concerns the explicitly approved uncertainty contract, not a claim that xUnit executes Alternative. Affects criterion 5, package 4 and package 6's complete acceptance.

The same uncertainty policy also fails for legitimate **private NUnit providers**: the shared predicate at `:551-553` requires `public static` for every framework, although the [primary NUnit TestCaseSource documentation](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html) includes private static enumerable source methods. The primary [MSTest data-source documentation](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests-data-driven) requires public static members; no analogous private-source claim is made for MSTest.

Additional real-framework reproduction: `temp/final-audit-8-nunit/Repro.slnx`, NUnit **3.14.0**, same SDK/configuration, with this input:

```csharp
using NUnit.Framework;
using System.Collections.Generic;
[TestFixture]
public sealed class Tests
{
    [TestCaseSource((string)null)] public void Case(int value) { }
    private static IEnumerable<object[]> PrivateRows() => [];
    private static void PrivateHelper() { }
}
```

Restore succeeded; the CLI review completed exit 0, run `20261001T104022Z-c4342ed2`, detected **PrivateRows and PrivateHelper**. The null source name enters the statically unavailable-name branch `:343-354` in a known source type; PrivateRows must be treated conservatively as a plausible NUnit provider, while the private void helper stays a candidate. Replacing null with the unresolved static name `"UnknownRows"` exercises `:358-370` and gives the same two findings (run `20261001T103944Z-c6be76c8`). These are binding-uncertainty inputs; the audit does not claim their data source resolves at runtime. Provider plausibility must follow supported framework data signatures/visibility, including inherited sources, rather than suppressing all ordinary private methods.

## Criterion matrix and boundaries

| Criterion | Independent evidence and conclusion |
| --- | --- |
| 1 — Long tests/string fixtures | CodeSizeCandidatesAnalysis applies project options before all three collectors; FastTests at CodeSizeCandidatesAnalysisTests:384 cover the original extremes and raw-string distinction. Corrected practical reports show LongScenario 1009 token-start lines and StringFixtureTests 3118 physical lines, with no inflated raw-string member. Met. |
| 2 — Seven maintenance analyses | No remaining IsTestProject exclusion in their candidate paths; the sole production-only selector remains MissingTestEvidenceCandidateSelector. Duplicate/structural/forwarding/non-ASCII tests include helpers and skipped declarations. Dead-code includes test projects but its framework safety is incomplete under criterion 5. |
| 3 — Statistics | Size and control-flow loops select and measure each project separately. Inclusive/tie/extreme FastTests and MethodControlFlowOutliersIntegrationTests:18 verify independent populations/options (production remains 2 findings while test P99/P50 changes 1 to 2). Met. |
| 4 — Full clusters | StructuralDuplicationCandidatesAnalysisTests:328 and ReviewRunnerTests:371 verify production/test/mixed completeness and test-only change selection. The fingerprint prepass retains distinct-owner requirements; full ordinal normalized strings remain authoritative at StructuralDuplicateDetector:275-284, with unchanged containment. No truncation or role split found. Met. |
| 5 — Framework protection | Metadata/derived/inherited tests, hooks, fixtures, interfaces, skipped tests, providers, lookalike rejection, both API modes and broad reachable-project exclusions inspected in the collector, indirect index and matrix tests. Unknown names scan inherited members but the plausible-member policy is incomplete; unknown types expose reachable-area reasons. **Not met: P1/P2 above.** Emitted references alone do not prove actual modern MSTest identity. |
| 6 — Roles/generated boundaries | ReviewSourceClassifier:57-85 and its tests expose reference/name/path/no-marker reasons. ReviewRunner and FindingDraft.SubjectSymbols/ReviewFinding.SubjectOccurrences preserve origin independently of contextual RelatedSymbols; validation requires nonempty related subjects. Generated/path filters remain; no new marker or per-method role system found. Met. |
| 7 — Reports | Writer:87-125 emits six indexes and only nonempty area/view analysis files; FormatRelated:666-697 uses whole-view visibility. Source/evidence/occurrence roles, MTE production origin, representative pair counts, sort and partial/unbounded/full instructions checked against tests and generated output. Rechecked **89 relative links in 25 Markdown files, 0 missing**, corrected root counts 2/9/1. Met. |
| 8 — Baseline | HostAdapterIntegrationTests:464 verifies all eight analyses, no-baseline views, unchanged newly enabled test scope in all-findings only, complete mixed partial cluster after test change, byte-identical baseline. Existing MTE host case at :323 covers snapshot-wide added/changed/deleted C# and non-C# behavior. Hash calculation remains source-based. Met. |
| 9 — Product/publication | Writer owns a temporary directory and publishes one final rename, with cancellation checks and failure cleanup; host/publication tests preserve previous runs on errors/cancellation. Review remains exit 0 with findings (also observed in all audit repros); no refactoring/build-failure conversion introduced. Met. |
| 10 — Practical audit | Package-7 original flat rerun/evidence, generated final reports and all reported subject identities checked: 12 findings, production 2/tests 9/mixed 1, all eight analyses active, every present signal assessed, separate tests-only boundary explicit. Recorded 12.573 s/74.5 MiB samples are not a new measurement by this auditor or a release/larger-workload claim. The two 100-statement-owner regression remains bounded; 1005-by-1005 is unmeasured. Met. |
| 11 — Test options | Shared descriptor validation, ResolveTestOptions inheritance, ForProject selection, effective/provenance reporting inspected. Validator tests:114-164 cover partial/empty, disabled invalid/duplicate/type/range/unsupported cases; size/control-flow integration cases observe higher/lower/percentile selection without source changes. Met. |
| 12 — Complete defaults | Generator enumerates registry descriptor defaults and supported TestOptions; repository file includes all eight, arrays/API mode and only two test objects. DefaultReviewConfigGeneratorTests compare every default; Bootstrap/ReviewAnalysisServiceRegistrationTests compare root config to registered descriptors; ZeroConfig tests preserve user files in both commands. Met. |

All concept non-goals were checked against the changed-file set and implementation: no new quality analyses, runner execution, external/unloaded test scans, separate CLI/baseline/opt-in, test suppression/Top-N, higher defaults/multipliers, new control-flow parameters or profiles, method-level roles, loosened generated/path boundaries, new workflows/scheduler, old-run mutation, recursive tests-for-tests or automatic refactors/build-breaking review findings. The MTE active-root/path semantics are unchanged. No further non-goal violation was found. Current documentation was checked; its full MSTest/protection claims require correction alongside P1/P2, not a new product feature.

Existing final gate artifacts were inspected: `temp/build.log` records 0 warnings/errors, `temp/test-fast.log` and `TestResults/FastTests.trx` 343/343, `temp/test-integration.log` and `TestResults/IntegrationTests.trx` 111/111. Full gates were not rerun for this read-only audit. New verification was limited to the actual-framework CLI repros and report-link resolution. Passing prior gates does not resolve these reproducible contract failures. Documentation diff review and `git diff --check` passed for this audit-result commit.

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

## Correction verification (sole post-audit implementer)

The audited findings were reproduced against the pre-correction host before editing: genuine MSTest.TestFramework 4.3.3 (`temp/final-audit-8-mstest`, run `20261001T104444Z-3c3f35fd`) reported the `T:ValidTests` type; xUnit v3 3.2.2 (`temp/final-audit-8-xunit`, run `20261001T104446Z-8943bc13`) reported `Alternative` and `PrivateHelper`; NUnit 3.14.0 (`temp/final-audit-8-nunit`, run `20261001T104448Z-9a3abf3e`) reported `PrivateRows` and `PrivateHelper`. These were the same original audit inputs and configurations.

The corrected host was then run against actual framework metadata for both MSTest identity generations and the original xUnit/NUnit repros. MSTest 3.11.1 was restored from the existing global package cache into a separate Git-ignored input, without changing repository package references. Its `Microsoft.VisualStudio.TestPlatform.TestFramework.dll` identity was confirmed read-only as `Microsoft.VisualStudio.TestPlatform.TestFramework, Version=14.0.0.0`. The real 3.11.1 CLI run `20261001T105900Z-2aa0b1f1` and real 4.3.3 run `20261001T105641Z-72508a19` each report only `M:ValidTests.OrdinaryHelper`; the class, test method, and initialize hook are protected. The actual xUnit v3 run `20261001T105643Z-210e6bfe` reports only `M:Tests.PrivateHelper`; `Alternative` is no longer reported. The actual NUnit run `20261001T105645Z-7d80e03d` reports only `M:Tests.PrivateHelper`; the private static enumerable source is no longer reported. All four runs completed with exit 0 and one finding apiece. No repository package upgrade was made.

Permanent FastTests separately exercise emitted metadata under both legacy and current MSTest assembly identities, inherited array providers under ambiguous xUnit names, private NUnit enumerable providers for both null and unresolved source names, and a user-defined derived xUnit `MemberDataAttribute` whose real metadata base is xUnit v3. The latter checks inherited ambiguous `Task<IEnumerable<object[]>>` overloads and other inherited async providers. Provider uncertainty now follows framework-supported member visibility and collection signatures, including array values, xUnit v3 async wrappers, and NUnit async capabilities by framework assembly version. Ordinary scalar/void helpers remain candidates. The unresolvable-source-type project-area exclusion, type-local scope, unrelated-project candidates, semantic lookalike rejection, existing hooks/fixtures, and both API modes remain covered.

Final verification after the code and regression changes: `pwsh -NoProfile -File ./scripts/build.ps1` passed with **0 warnings and 0 errors**; `pwsh -NoProfile -File ./scripts/test-fast.ps1` passed **344/344**; `pwsh -NoProfile -File ./scripts/test-integration.ps1` passed **111/111**; the affected dead-code suite passed **31/31**; and `git diff --check` passed. Criteria 4, 5, and 6 are closed with this correction; the independently verified remaining criterion matrix and non-goal review remain as recorded above. No unresolved correction finding remains.
