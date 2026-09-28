# Milestone 1 audit

Status: findings recorded; Milestone 1 audit remains open.

Reviewed the changes since `f24f5e9`, the M1 leaf checklists, `Konzept.md`, the affected tests and current-state documentation, and the repository rules. Markup capture is gated by the exact configured rule ID in `SolutionLoader`; a no-rule integration case uses an oversized markup file to establish that capture is skipped. Snapshot reads happen before rules run and are retained as immutable path/text records. The failure path disposes the workspace and propagates `AnalysisFailedException`; host integration coverage verifies that a markup read failure publishes no run.

`SolutionReferenceIndex` records production/test and generated-source provenance, method groups, containing-type references, and self-reference status. Missing projects, compilations, syntax roots, or semantic enumeration fail index creation. Candidate selection and test/generated exclusions are not applied in this shared utility. Focused tests cover test-project and generated-file references, method groups, symbol-local uncertainty, complete zero references, and a global no-C#-project failure. Tests were read but not run as part of this read-only audit.

## Findings

- **P2 — AdditionalDocument paths can cross a reparse-point ancestor.** `MarkupSnapshotLoader.cs:53-67` feeds Roslyn documents directly to `AddCandidate`. `AddCandidate` checks only whether the final file itself has `ReparsePoint` (`:153-159`), then canonicalizes the path (`:165`) and accepts a target that remains inside the project root (`:166-179`). A markup AdditionalDocument reached through an in-root directory symlink/junction is therefore captured from the resolved target even though discovery must not follow reparse points. The existing reparse test covers directory traversal only, not this document-backed path. Apply the reparse boundary to every path component before accepting document paths.

- **P2 — AdditionalDocument paths bypass the nested-project exclusion.** The document pass at `MarkupSnapshotLoader.cs:53-67` adds candidates before directory enumeration. The nested-project check exists only in `EnumerateMarkupFiles` (`:102-105`), so a markup AdditionalDocument under a nested foreign project is captured if the outer C# project explicitly includes it. The current nested-project test covers only filesystem enumeration. Apply the same nested-project boundary to document-backed candidates.

- **P2 — Custom build output directories are not excluded.** `SkippedDirectoryNames` at `MarkupSnapshotLoader.cs:17-24` covers `.codex`, `.git`, `bin`, `node_modules`, and `obj`, but not project-configured output/intermediate directories under other names. The traversal at `:124-140` scans those directories and captures matching markup extensions. This falls short of the concept's build-output exclusion when a project sets a custom output path. Derive and exclude the analyzed projects' actual output directories (or otherwise establish that custom output roots are outside discovery).

No production or test files were changed. The M1 audit and aggregate milestone checkbox in `roadmap.md` must remain open until these findings are resolved and re-audited.
