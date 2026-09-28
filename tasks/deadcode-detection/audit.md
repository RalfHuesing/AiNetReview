# Milestone 1 audit

Status: verified; all M1 findings are resolved and the milestone is closed.

The audit reviewed the changes since `f24f5e9`, both M1 leaf checklists, `Konzept.md`, the affected tests and current-state documentation, and the repository rules. Markup capture is gated by the exact configured rule ID in `SolutionLoader`; a no-rule integration case uses an oversized markup file to establish that capture is skipped. Snapshot reads happen before rules run and are retained as immutable path/text records. The failure path disposes the workspace and propagates `AnalysisFailedException`; host integration coverage verifies that a markup read failure publishes no run.

`SolutionReferenceIndex` records production/test and generated-source provenance, method groups, containing-type references, and self-reference status. Missing projects, compilations, syntax roots, or semantic enumeration fail index creation. Candidate selection and test/generated exclusions are not applied in this shared utility. The M1-T2 checklist and its recorded test gates are complete.

## Resolved findings

- **P2 — AdditionalDocument paths can cross a reparse-point ancestor.** Added a regression using an AdditionalDocument reached through a directory symlink. AdditionalDocument paths are now boundary-validated but only enter the snapshot through filesystem discovery, which checks path components and does not follow reparse points. The test failed before the fix and passes after it.
- **P2 — AdditionalDocument paths bypass the nested-project exclusion.** Added a regression for an AdditionalDocument under a nested foreign project. Candidate capture now uses the same nested-project boundary as directory traversal. The test failed before the fix and passes after it.
- **P2 — Custom build output directories are not excluded.** Discovery now excludes each project's output directory from `Project.OutputFilePath`/`OutputRefFilePath` and the generated MSBuild editorconfig path for its intermediate directory. The regression puts markup in custom output and intermediate locations; it failed before the fix and passes after it.

Verification after correction: all 48 IntegrationTests and 89 FastTests passed; `scripts/build.ps1` completed with zero warnings and zero errors; `git diff --check` passed. The complete markup discovery remains conditional on the configured `dead-code-candidates` rule. No M1 findings remain open.
