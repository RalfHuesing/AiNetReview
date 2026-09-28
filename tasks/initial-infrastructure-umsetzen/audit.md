# Final audit — initial infrastructure implementation

Date: 2026-09-28
Correction base: `dd2e8e43173691a3ed8b118f7a75db6475465b7b`

The correction worktree was reviewed against the ready implementation concept, approved concept, Epics 1, 3, and 4, applicable repository rules, current-state documentation, and the original audit findings. All four findings below are resolved. The final build, FastTests, IntegrationTests, performance gate, and self-analysis were rerun against the corrected implementation.

## Findings and resolution

### [P2] Backticks in evidence snippets can break report Markdown — resolved

Evidence snippets now use a code-span delimiter one backtick longer than the longest backtick run in the content. Edge spaces or backticks receive the CommonMark padding needed to keep closing delimiters separate from snippet content. The regression test checks snippets containing both leading and trailing backtick runs; it would fail against the original single-backtick output.

### [P2] Invalid path characters can escape input-error classification — resolved

`ProjectPathResolver.ResolveRelative` translates `ArgumentException` from path canonicalization into `InvalidReviewInputException`. FastTests cover NUL characters in both `solution` and `outputDirectory`. Process tests encode each NUL in JSON and verify `INVALID_INPUT`, exit code 2, empty stdout, and no report publication.

### [P2] Performance test samples current memory instead of measuring peak private bytes — resolved

The Windows performance gate now calls `GetProcessMemoryInfo` after process completion and reads `PROCESS_MEMORY_COUNTERS_EX.PeakPagefileUsage`, the process lifetime peak commit charge. Windows documents this counter as the peak committed private memory for the process ([Microsoft documentation](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex)). The test rejects a zero measurement. On Windows 10.0.26200.0, the rerun measured 0.26 GiB for the 180,024-line load in 7.07 seconds; exit was 0 and report publication succeeded, within the 10-minute and 6-GiB limits.

### [P2] Operational log events omit the command field — resolved

The host enriches every event with the command. Startup no longer consumes the command as a message-template token, so it remains an explicit event property. The completion event pushes the known `RunId` into the log context as a property. Process tests verify the command property on every event for a successful invocation, the run ID property on its completion event, the `unknown` command on startup, and command metadata on an invalid-input event.

## Verification evidence

- The required failing-first regressions were observed: the Markdown assertion failed on the old rendering, and both NUL path cases threw raw `ArgumentException` instead of `InvalidReviewInputException`.
- `pwsh -File ./scripts/build.ps1`: passed, 0 warnings and 0 errors.
- `pwsh -File ./scripts/test-fast.ps1`: passed, 61/61.
- `pwsh -File ./scripts/test-integration.ps1`: passed, 32/32.
- `pwsh -File ./scripts/test-performance.ps1`: passed, 1/1. Windows 10.0.26200.0, .NET SDK 10.0.400, 32 logical CPUs, and 125.65 GiB available RAM; 180,024 non-comment C# code lines across 6 documents and 6 projects; production EXE 7.07 seconds, 0.26 GiB lifetime peak private commit charge, exit 0.
- Self-analysis: `src/AiNetReview/bin/Debug/net10.0/AiNetReview.exe review --config <repo-root>/ainetreview.json` analyzed `AiNetReview.slnx`, published a zero-finding report, and exited 0 (run `20260928T084401Z-5f5b15ba`). The temporary root config and report were removed.
- The production registration remains limited to `TemplateNoOpRule`; the fixture rule stays test-only. Existing tests continue to cover atomic report publication and cleanup, prior-run preservation, source materialization, compilation checks, and finding validation.
