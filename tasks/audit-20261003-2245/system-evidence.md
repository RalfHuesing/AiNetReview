# System-level evidence

Independent plan: baseline-and-coverage.md. Source was reviewed before signal priorities were adopted. Both verification copies were checked byte-identical against tracked originals after their test runs (copy-integrity.txt). The later final original-state check detected a parallel byte-identical rename of the invoked audit workflow; final-integrity.txt records this separately. No product or report content changed.

## Executed checks

Environment: Windows 10.0.26200, .NET SDK 10.0.400, MSBuild 18.9.6, runtime 10.0.11. global.json requests 10.0.203 with latestFeature roll-forward. No environment or dependency files changed.

| Check | Actual command / context | Result | Evidence |
| --- | --- | --- | --- |
| Original build | `dotnet build .\AiNetReview.slnx --nologo -v minimal` in original root | Exit 0, 0 warnings/errors | build-original.log |
| Verification-copy build | Same command in verification-copy | Exit 0, 0 warnings/errors | build-copy.log |
| FastTests | `dotnet test tests\AiNetReview.FastTests\AiNetReview.FastTests.csproj --no-build --no-restore --logger 'trx;LogFileName=FastTests.trx' --results-directory ..\test-results` in verification-copy | 370 passed, 0 failed/skipped | test-fast.log; test-results/FastTests.trx |
| Regular IntegrationTests | Same pattern for IntegrationTests with `--filter 'Category!=Performance&Category!=Audit'` | 110 passed, 0 failed/skipped | test-integration.log; test-results/IntegrationTests.trx |
| Fresh source self-review | Executed by `ProcessInvocation_WithRepositoryConfigurationPublishesAnIgnoredTimestampedRun` during the integration suite | Published complete run; false missing-test signal repeated | verification-copy/audit-reporting/20261003T205147Z-6d061348/ |
| NuGet advisories | `dotnet list .\AiNetReview.slnx package --vulnerable --include-transitive --no-restore` in verification-copy | Exit 0; no vulnerable packages reported for five projects against configured sources | dependency-vulnerabilities.log |
| Fresh Release-only test | `dotnet test tests\AiNetReview.IntegrationTests\AiNetReview.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~ProcessInvocation_WithRepositoryConfigurationPublishesAnIgnoredTimestampedRun' --logger 'trx;LogFileName=ReleaseOnly.trx' --results-directory ..\test-results` in release-verification-copy, with no Debug outputs | Build succeeds; selected test fails at hard-coded Debug executable assertion, exit 1 | test-release-only.log; test-results/ReleaseOnly.trx |
| Structural scaling | Build StructuralProbe.csproj; independent processes `dotnet .\bin\Debug\net10.0\StructuralProbe.dll 100`, `200`, `400` | All completed, one finding each; table below | performance/Program.cs; performance/probe-*.log |

The first original build log was accidentally redirected to `C:/Daten/Entwicklung/Ralf/build.log`; the audit-owned file was immediately moved into this directory as build-original.log. No protected source was rewritten. Ordinary build outputs in original bin/obj are allowed by the audit contract; all test fixtures and report generation were isolated in WORK_DIR.

## Structural duplication scaling

Synthetic compilable C# input: one class, two distinct methods, each with n sequential `value += i;` statements with increasing integer literals and a return. Compilation is validated before measuring. Only the unchanged structural-duplication analysis is measured; solution loading, compilation warm-up, rendering and publication are outside the stopwatch. One maximal duplicate with both occurrences survives in each case. Each case runs in a separate process, not concurrently. This shape is hypothetical input, not an observed production regression.

| Statements per method | UTF-8 source bytes | Analysis seconds | Cumulative managed allocation delta (bytes) | Findings |
| ---: | ---: | ---: | ---: | ---: |
| 100 | 2,922 | 0.8393372 | 631,012,216 | 1 |
| 200 | 5,922 | 4.4222839 | 4,830,023,680 | 1 |
| 400 | 11,922 | 32.0658436 | 37,257,520,200 | 1 |

No timeout occurred: the probe's 45-second cancellation guard was unused. Measurements are single samples on this machine; no frequency estimate, OOM, peak-memory measurement or numeric release-budget violation is claimed. `GC.GetTotalAllocatedBytes(true)` measures cumulative managed allocations, not retained or peak memory. Primary API reference: https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes?view=net-10.0 . NuGet query semantics: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list . Only public documentation and package advisory queries were sent externally, no repository source.

Cause: StructuralDuplicateDetector.cs:191-235 builds incremental fingerprints for all intervals; :238-302 replays NormalizeFragment for each common interval; :478-500 traverses it from scratch and materializes a complete exact string; :305-347 groups full occurrences and suppresses containment afterward. This produces repeated normalization/allocation of nested intervals that ultimately disappear. Full exact matching, additional uncontained occurrences, all locations and owner/type distinctions remain mandatory; skipping or capping inputs is not a compatible remedy.

The existing 100-statement correctness case at StructuralDuplicationCandidatesAnalysisTests.cs:494-508 checks maximal suppression, not scaling. The separate InfrastructureLoadTests.cs:38-64 uses 180,000 constant declarations and enables only method-control-flow-outliers. It does not exercise this executable-fragment shape or the structural analysis. The performance gate was not rerun in this audit; its source was inspected.

## Release verification boundary

scripts/release.ps1:137-171 runs Debug/default build and test scripts (performance optional), before changing Version at :285-289. This is a real mitigation when the documented release script is followed. .github/workflows/release.yml:3-7 triggers directly for v* tags and :23-62 publishes Release/win-x64/self-contained, archives and releases without running tests or the archive. There is no other workflow in the checkout. No repository evidence proves that tag publication is restricted to the validated script, nor did the audit inspect remote branch/tag policies.

HostProcessIntegrationTests.cs:104-114 hard-codes src/AiNetReview/bin/Debug/net10.0. A clean Release-only checkout therefore fails the selected process check despite a successful Release build. On a reused checkout, the same test can instead exercise an old Debug artifact. This is a demonstrated verification defect, not evidence that the shipped executable currently fails. Recommended later intervention: select the intended current artifact explicitly in tests, run suitable gates in the tag workflow, and smoke-test the produced self-contained artifact before releasing it. Keep the release platform, distribution shape, CLI/logging and complete report contracts intact.

## Blind-spot pass

- Root production registry, descriptor defaults, root ainetreview.json and bootstrap generation: ten entries and enabled defaults align; registration/config integration tests pass. No dynamic analysis loading or Core-to-host dependency found.
- CLI -> config -> snapshot -> analysis -> finding validation -> maps -> atomic publication -> success JSON: inspected with subsystem reviews; tests cover failure/cancellation, locks, concurrent publication, empty runs, scope, and prior-run preservation. No partial publication defect demonstrated.
- Semantic graphs and uncertainty: missing-test lead independently challenged; safe symbol ownership is the important invariant rather than replacing every comparer mechanically.
- Dependencies/security: pinned central versions, no vulnerability advisory result. This does not certify licensing, future advisories, native tools or safety of untrusted MSBuild targets. No external/production execution was attempted.
- Build/release/testing: Debug tests are green; Release-only target failure reproduced. Existing large-line gate does not cover structural method-fragment scaling. Remote CI and published archives were not exercised.
- Agent navigation: Navigator index covers five net10.0 projects and 147 Roslyn documents (20 generated, 66 test); source paths, DI and runtime semantics were checked separately. Large classes/cycles alone are not failures. Concrete dropped semantic edges and tests that select the wrong artifact can mislead later agents despite green default gates.
