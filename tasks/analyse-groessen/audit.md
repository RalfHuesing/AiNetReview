# C4 closing audit — required documentation finding (2026-09-30)

Reviewed C1 `33c0338`, C2 `79bf4e2`, the C2 corrections `72f52b0` and `1bbe656`, the final C2 audit `7ce5f70`, and C3 `0b489fb` against the size and Core concepts, implementation, tests, configuration, reports, and current-state documentation. The size analysis reuses `CodeLineMetrics.CountExecutableDeclaration`, `CodeLineMetrics.CountOwnTypePart`, and `ControlFlowMetrics.Measure`; its member, class, and file selectors implement the independent paths and concept defaults. The collectors use eligible loaded C# documents, per-project path deduplication, symbol filtering, and partial-type aggregation. Finding identity, snapshot evidence, changed-file selection, report signals, default registration, and the unchanged control-flow analysis are covered by code and tests. No production-code finding emerged from this audit.

Required documentation finding: [`docs/review/findings.md`](../../docs/review/findings.md) lists `code-size-candidates` first in the production registry, then says “The first analysis compares executable method declarations ...” and describes the 8-decision / 4-nesting control-flow thresholds. That description belongs to `method-control-flow-outliers`, not the now-first-listed size analysis. Correct the referent and perform a separate independent follow-up audit before closing C4.

Full normal gates after the last code commit: FastTests **259/259**, IntegrationTests **97/97**, no skips; `dotnet build AiNetReview.slnx --no-restore` **0 warnings, 0 errors**; `git diff --check` passed before this documentation commit. The historical intermittent `Directory.Move` `IOException` did not occur and remains the accepted residual finding; no tests were skipped.

Manual audit used temporary ignored profiles `audit-targets/ainetreview-size-c4.json` and `audit-targets/ainetlinter-size-c4.json`, both explicitly `enabled: true` with only `code-size-candidates` and the exact seven concept defaults. Both `scripts/test-audit.ps1` runs passed their Audit integration test (1/1 each), published a `code-size-candidates.md` report, and left the existing disabled profiles unchanged. Both central runs had no baseline, so their `changed-files` and `all-findings` counts agree. AiNetLinter was read-only.

| Target and run ID | Members | Classes | Files | Total |
| --- | ---: | ---: | ---: | ---: |
| AiNetReview `20260930T005126Z-24ded0d8` | 13 | 5 | 0 | 18 |
| AiNetLinter `20260930T005151Z-70bb0b66` | 1 | 36 | 0 | 37 |

Spot checks of published signals against the concept defaults: AiNetReview's `DuplicateCodeDetector.CollectAsync` has 82 code lines, 9 decisions across 9 constructs, nesting 4, and meets the member minimum 80 and project P90 value 45, so the relative member path is stated. AiNetReview's `MarkupSnapshotLoader` has 361 code lines and meets the class minimum 300 and P90 value 216. AiNetLinter's `AssemblyAnalysisDispatcher.ExecuteAsync` has 80 code lines and 9 decisions across 9 constructs and meets its member P90 value 28. Its `RuleRegistry` aggregates 1320 code lines across six declaration parts and correctly shows both the relative class path (minimum 300, P90 value 152) and extreme class path (800). Neither repository produced a file candidate at the defaults. None of these signals asks for a split solely to lower a number.

C4 and the size-task aggregate remain open for the documentation correction and independent follow-up audit. C2 used two correction rounds; this is the C4 first audit, with one confirmed narrow finding.
