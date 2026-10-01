# Build and Tests

## Prerequisites

- .NET 10 SDK (pinned via `global.json`, version `10.0.203` or compatible feature release)

## Solution Structure

The shared source-classifier tests also verify the reason for each existing test-project marker. `ReviewRunnerTests` verify that every loaded C# project appears in the run's classification overview, including a project with no recognized test marker and a project with no findings, and that finding occurrence roles come from represented symbols independently of evidence files. Multiple represented occurrences remain separate even when they share the same owner and line.

`AuditSourceContextTests` cover immutable snapshot preparation for outer, nested, partial, and delegate source types; per-project identity for linked files; production/test roles; alias, generic, extension-method, and method-group normalization; exact reference and uncertainty spans, lines, and columns; exact declared-symbol-to-outer-type identities; exclusion of generated-document and attribute-marked symbol origins from references and uncertainty, plus generated targets; file-context references outside type declarations; and stability after the backing source file changes. `AuditFindingPackagesTests` cover complete unique assignment, stable IDs under reordered findings and subject occurrences, original finding and role preservation, common area sets, type and file fallbacks, independent test-type assignments, partial targets and uncertainty, property-only relations, direct non-transitive package links, context-only test types, empty views, and baseline selection. A `ReviewRunnerTests` case verifies that both package views are handed off internally with unchanged file-baseline selection.

The solution `AiNetReview.slnx` contains five projects:

- `src/AiNetReview.Core/`: Core library for configuration, analysis, analyses, and current finding results.
- `src/AiNetReview/`: Host executable with DI composition, zero-config `review` command, and logging.
- `tests/AiNetReview.TestKit/`: Shared test support infrastructure (e.g. isolated temp directory lifecycle and test helpers).
- `tests/AiNetReview.FastTests/`: Unit and component test suite.
- `tests/AiNetReview.IntegrationTests/`: End-to-end and host integration test suite.

Core, Host, and TestKit expose their internal members to both test assemblies through `InternalsVisibleTo`. This supports tests of internal components as the implementation grows.

The dead-code framework matrix uses emitted metadata references with the framework assembly identities expected by the classifier. It covers xUnit v2 and v3 (including v3 `IFactAttribute`), NUnit, and both legacy and current MSTest assembly identities; derived and inherited tests/hooks; skipped or explicit declarations; fixture and lifecycle contracts; named and type-based data providers; MSTest display-name callbacks and assembly fixtures; local lookalike rejection; private-helper candidacy; both API modes; and local versus project-area uncertainty. Ambiguous xUnit names cover plausible inherited array providers, while unresolved NUnit names cover private static enumerable providers and retain ordinary private helpers as candidates. The broad-exclusion case verifies project-relative scope paths and reasons, while the existing dead-code tests continue to verify generated and cross-project reference protection.

FastTests cover the shared source classifier's recognized and rejected test-project names, paths, and reference assemblies; the shared `FastTestReferences` fixture helper filters all recognized test-framework assembly families through that same classifier while retaining ordinary runtime references; generated file path and header markers; generated symbol attributes; and checked project-root-relative path conversion. Shared semantic reference tests cover cross-project production and test uses, method groups, generated C# documents, containing-type self-reference provenance, complete zero-reference results, symbol-local binding uncertainty, global coverage failure, and unchanged results from the existing analysis. Review analysis tests verify that production and test code share the duplicate and structural comparison groups, while generated documents and symbols remain excluded. Dead-code tests cover internal executable entry points under both API modes, synchronous and asynchronous startup methods, configured nested startup types in partial containing types, explicit partial `Program` with top-level statements, continued unused-method findings, unrelated types, and library `Main` behavior. `BaselineReaderTests` validate optional, valid, and malformed source baselines. `ReviewRunnerTests` verify complete baseline-independent analysis results, changed-file selection across all finding evidence, multi-file clusters, and symbol relationships across analyses without joining unrelated findings from the same file. Missing-test-evidence regressions cover nullable-warning suppression and nested parentheses across executable body variants, retained invocation and accessor edges, constructor initializer edges, direct and indirect test paths through test helpers, and complete findings with a `JsonElement.GetString()!` helper. Configuration tests verify generated defaults and invalid values for missing-test-evidence thresholds. `MarkdownReportWriterTests` verify review guidance placement, required investigation and evidence-based classification without automatic changes, both report views, exact finding/project/file counts, same paths in separate projects, deterministic finding order, Markdown escaping, representative-path omission from ordinary labels, one-based ordinary, cluster, and forwarding locations, empty views, related-analysis visibility across the complete current view, full-audit guards on standalone reference reports, shell quoting, and isolation of edits to the working view; missing-test-evidence coverage checks both category labels, uncertainty propagation through known downstream calls without inventing paths, generated intermediate evidence in the shortest indirect path, its snapshot-wide selection, representative-source baseline status, test-only and deleted-C# snapshot triggers, attribution-uncertainty explanation, and its report limits; forwarding-path coverage checks call-order evidence, test-project inclusion, and its three structural counts; structural-duplication coverage verifies production/test occurrences in one complete group, exact fragment grouping, recursive type signatures, normalization, barriers, excluded top-level/lambda/anonymous-method owners, containment, owner identities, and occurrence evidence. The code-size integration test verifies that each candidate-kind review question appears once per report while each finding retains its selection reasons and measurements, and checks the shared control-flow legend and criterion/threshold wording. Method-control-flow and missing-test-evidence report tests also verify that legend. Report-contract coverage checks the missing-test OR gates and global uncertainty wording, nearest-rank/floor control-flow cutoffs, code-size token-line definitions and selection formulas, duplicate similarity presets, structural-fragment containment wording, and code-span-safe shortest indirect-path IDs and locations. `SolutionLoaderTests` cover conditional markup capture, snapshot stability, AdditionalDocument reparse-point and nested-project boundaries, custom output and intermediate directories, unreadable inputs, and markup size limits.

## Building

Build the solution using the PowerShell build script, which logs the full console output to `temp/build.log`:

```powershell
pwsh -File ./scripts/build.ps1
```

TreatWarningsAsErrors and Nullable reference types are enabled across all projects in `Directory.Build.props`.

`MarkdownReportWriterTests` verify the two Markdown audit-map views, exact package counts, direct stable finding anchors, source and symbol navigation, context-area anchors, exact project/file/line/occurrence assignment for multiple production and test owners, explicit empty views, and that changed-files output omits excluded finding details. Host integration tests exercise generated package links for structural fragments and cross-project findings, external audit output roots, production CLI publication, and the existing cancellation/failure guarantees for complete atomic runs.

## Host Logging

The host initializes Serilog before command parsing. Its only sink writes `ainetreview-<date>.log` under the host executable's `logs/` directory, independent of the working directory. Every event carries the command; completion also carries the run ID. Files roll daily and at 10 MiB, retain at most 30 files, and allow concurrent host processes to write. If the directory or active log file cannot be opened for writing, the host exits with code `4` and writes a `LOGGING_FAILED` JSON error to stderr without writing to stdout. Process-level coverage is in `HostProcessIntegrationTests`.

The host integration tests also run the production executable against generated projects and check JSON stream shape, exit codes, invalid path classification (including NUL characters), publication on a complete empty run, no publication after analysis failure, command and run-ID log metadata, executable-relative logs, size rotation, and concurrent processes. Baseline process tests verify config bootstrap, a standalone run without an audit report, hashes for C# and configured markup files without findings, complete replacement after source changes and deletions, and preservation of the prior baseline after load or publication failures. A central baseline process test supplies the exact target root, captured config, and central output directory. The manual audit test also invokes the real AiNetReview executable with its captured central config. One normal IntegrationTests case starts the Debug executable with the repository root as the project path, then checks all three report areas and both views, direct root-index links, the quoted baseline command, timestamped publication, and preservation of prior runs under the git-ignored `audit-reporting/` directory. A separate generated project with no methods still exercises a complete empty run. `HostAdapterIntegrationTests` runs the production dead-code, duplicate-code, structural-duplication, indirection-drift, and missing-test-evidence analyses through the host. `ReviewCommand_AllAnalysesKeepBaselineAndMixedPartialTypeContractsAcrossTheHost` loads linked production and test projects with all eight registered analyses enabled, checks complete no-baseline views and mixed partial-type occurrences, then verifies that a newly enabled analysis over unchanged test paths appears only in `all-findings`, and that changing a test occurrence selects the complete mixed finding. `ZeroConfigIntegrationTests.Commands_PreserveAnExistingUserConfiguration` verifies that both commands leave an existing user config byte-for-byte intact. The duplicate-code case checks default and configured thresholds, links to every cluster member across projects, compact cluster signals, omission of empty analysis files, current source snapshots, and no new publication after analysis failure or cancellation. The dead-code case checks grouped findings, protected public API, repeated audits, omission of an empty analysis file, and no new report after cancellation. The indirection-drift case checks the registered configured analysis, ordered path output in both report views, empty-file omission, and no publication after analysis failure or cancellation. The missing-test-evidence case checks both categories, production-origin classification with test-path context, the shortest indirect path, complete `all-findings`, no-baseline and unchanged-baseline behavior, added/changed/deleted C# paths, non-C#-only changes, empty results, and no publication after failure or cancellation. The fixture-analysis test repeats scans after changing source and options, checking current findings and preservation of earlier reports.

`.editorconfig` enables build errors for selected resource, async/task, threading, API-result, and regex-timeout defects. It explicitly disables design, complexity-related, context-dependent performance, and cancellation-forwarding diagnostics as build gates. The selected technical diagnostics apply to test projects and TestKit; suppress a verified false positive at its specific location.

## Running Tests

Run the test suites using the dedicated test scripts:

```powershell
# FastTests (dumps full console log to temp/test-fast.log and TRX to TestResults/FastTests.trx)
pwsh -File ./scripts/test-fast.ps1

# IntegrationTests (dumps full console log to temp/test-integration.log and TRX to TestResults/IntegrationTests.trx)
pwsh -File ./scripts/test-integration.ps1

# Explicit repository audit (does not run as part of the standard test scripts)
pwsh -File ./scripts/test-audit.ps1 -Target ainetreview

# Separate release-only infrastructure load gate (Windows, >=4 logical CPUs, >=16 GiB RAM)
pwsh -File ./scripts/test-performance.ps1
```

The normal IntegrationTests script excludes tests tagged `Category=Performance` and `Category=Audit`; the dedicated performance script runs that deterministic 180,000-line test against the production executable. On Windows it reads the process lifetime peak commit charge using `GetProcessMemoryInfo` and `PROCESS_MEMORY_COUNTERS_EX.PeakPagefileUsage`, alongside elapsed time, source document count, project count, and machine capacity. Release acceptance requires at most 10 minutes and 6 GiB peak private bytes. [Microsoft documents this counter as the lifetime peak commit charge](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex).

The manual audit script runs only the integration test tagged `Category=Audit`. It selects a local, Git-ignored `audit-targets/<name>.json`, where `repositoryPath` is relative to the AiNetReview repository root or absolute, `solution` is relative to that target repository, and `analyses` uses the normal analysis configuration format. The optional top-level `enabled` field defaults to `true`; setting it to `false` makes the script exit before starting `dotnet test`, running analyses, or publishing a report. Create one profile per repository and select it with `-Target <name>`; the target checkout does not need an `ainetreview.json` file. The audit publishes an `index.md` and Markdown files for analyses with findings under `audit-reporting/<name>/<runId>/` in AiNetReview. The baseline uses the selected profile's solution and analyses and writes `baseline.json` to `audit-reporting/<name>/`. A manual audit index provides `scripts/test-audit.ps1 -Target <name> -BaselineOnly` to update this centrally stored baseline without writing its config or baseline into the target repository. The index records the absolute target repository path and relative solution path; source links in analysis reports resolve to files in the target repository.

## Release Workflow

AiNetReview publishes standalone Windows x64 release archives through a GitHub Actions workflow (`.github/workflows/release.yml`) triggered by version tags (`v*`).

Automate the release process using the release script:

```powershell
# Validate working tree, run test suite, tag, and trigger GitHub release
pwsh -File ./scripts/create-release.ps1

# Dry-run mode to inspect planned steps without committing or tagging
pwsh -File ./scripts/create-release.ps1 -DryRun
```

The script verifies a clean working tree, synchronizes with `origin/main`, runs `FastTests` and `IntegrationTests`, updates `<Version>` in `src/AiNetReview/AiNetReview.csproj`, tags `vX.Y.Z`, and pushes the tag to GitHub where `.github/workflows/release.yml` publishes `AiNetReview-win-x64.zip`.

Agents and automation tools can inspect the static log files in `temp/` directly.
