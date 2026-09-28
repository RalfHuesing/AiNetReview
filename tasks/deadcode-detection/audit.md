# Milestone 1 audit

Status: verified; all M1 findings are resolved and the milestone is closed.

The audit reviewed the changes since `f24f5e9`, both M1 leaf checklists, `Konzept.md`, the affected tests and current-state documentation, and the repository rules. Markup capture is gated by the exact configured rule ID in `SolutionLoader`; a no-rule integration case uses an oversized markup file to establish that capture is skipped. Snapshot reads happen before rules run and are retained as immutable path/text records. The failure path disposes the workspace and propagates `AnalysisFailedException`; host integration coverage verifies that a markup read failure publishes no run.

`SolutionReferenceIndex` records production/test and generated-source provenance, method groups, containing-type references, and self-reference status. Missing projects, compilations, syntax roots, or semantic enumeration fail index creation. Candidate selection and test/generated exclusions are not applied in this shared utility. The M1-T2 checklist and its recorded test gates are complete.

## Resolved findings

- **P2 — AdditionalDocument paths can cross a reparse-point ancestor.** Added a regression using an AdditionalDocument reached through a directory symlink. AdditionalDocument paths are now boundary-validated but only enter the snapshot through filesystem discovery, which checks path components and does not follow reparse points. The test failed before the fix and passes after it.
- **P2 — AdditionalDocument paths bypass the nested-project exclusion.** Added a regression for an AdditionalDocument under a nested foreign project. Candidate capture now uses the same nested-project boundary as directory traversal. The test failed before the fix and passes after it.
- **P2 — Custom build output directories are not excluded.** Discovery now excludes each project's output directory from `Project.OutputFilePath`/`OutputRefFilePath` and the generated MSBuild editorconfig path for its intermediate directory. The regression puts markup in custom output and intermediate locations; it failed before the fix and passes after it.

Verification after correction: all 48 IntegrationTests and 89 FastTests passed; `scripts/build.ps1` completed with zero warnings and zero errors; `git diff --check` passed. The complete markup discovery remains conditional on the configured `dead-code-candidates` rule. No M1 findings remain open.

## Milestone 2 audit

Status: verified; the finding is resolved and M2 is closed.

The audit reviewed the feature diff since M1 ended at `2ec8bfc`, the M2 leaf checklists, the current concept and `docs/`, candidate and indirect-usage logic, configuration and production registration, deterministic rule/report ordering, the empty-report and publication-failure paths, and the focused tests. The implementation and current-state documentation consistently describe a repeatable audit with no source-comment suppression; every complete run reports its current candidates. Defaults and configured values for `apiSurface` and `entryPointAttributes` match the concept. Markup discovery remains conditional on this rule and applies the documented boundaries and limits. Global reference-index failures and unevaluable XAML fail analysis, and the host does not publish on cancellation or markup snapshot failure.

### Resolved finding

- **P2 — Self-recursive methods are treated as used.** Method selection now accepts coverage whose references are all marked `IsSelfReference`, so self-recursion alone is not evidence of external use. A focused regression with an externally used containing type failed before the fix because the method was omitted, then passed after it; external references still protect a method.

The retained `deadcode_test` audit fixtures in production Core include an unused method, an unreferenced type with a grouped member, and a type/method pair referenced only by a FastTests unit test. A production CLI run against `AiNetReview.slnx` (run ID `20260928T135619Z-b2e9b03d`, `closed_solution`) reported `deadcode_test_UnusedMethod` and `deadcode_test_UnusedClass`. It did not report `deadcode_test_UsedOnlyByUnitTest` or `deadcode_test_ReferencedByTest`. The test-only references therefore protect both declarations as specified. The temporary configuration was restored after the run; the report is retained under ignored `audit-reporting/deadcode_test-m2/` for local inspection.

Verification after correction: all 104 FastTests passed; all 49 IntegrationTests passed; `scripts/build.ps1` completed with zero warnings and zero errors; the production CLI audit completed and produced the fixture findings above; `git diff --check` passed. No M2 findings remain open.
