# Final audit — initial infrastructure implementation

Date: 2026-09-28
Base: `9fb0bd643d1c96b837995b16e338256f2f5f07dd`
Audited HEAD: `d672e67d4eb817a491022f05d82e1c9d770aec0c`

The final audit compared the implementation diff, current-state documentation, recorded verification evidence, ready implementation concept, approved concept, all four epics, and repository rules. The recorded build, FastTests, IntegrationTests, performance run, and self-analysis evidence covers the stated environments and commands. The production registration boundary, report publication transaction, older-run preservation, source materialization, path containment, and fixture-only rule registration are reflected in implementation and targeted tests.

The Final audit checkbox in `roadmap.md` remains open because these findings need resolution and verification.

## Findings

### [P2] Backticks in evidence snippets can break report Markdown

[MarkdownReportWriter.cs](../../src/AiNetReview.Core/Reporting/MarkdownReportWriter.cs#L198) inserts evidence snippets inside a single-backtick code span and applies `EscapeInline` at line 202. That helper backslash-escapes backticks (lines 235–253), but backslash escapes do not protect delimiters inside Markdown code spans. A valid source line whose snippet contains a backtick can therefore terminate the code span and alter the rendered report structure. The existing assertion in [MarkdownReportWriterTests.cs](../../tests/AiNetReview.FastTests/Reporting/MarkdownReportWriterTests.cs#L67) checks the emitted backslashes as text; it does not validate rendered Markdown. Epic 3 requires source and evidence content to be escaped so it cannot damage Markdown structure.

### [P2] Invalid path characters can escape input-error classification

[ProjectPathResolver.cs](../../src/AiNetReview.Core/Configuration/ProjectPathResolver.cs#L55) calls `Canonicalize` on user-provided `solution` and `outputDirectory` values without translating `ArgumentException` into `InvalidReviewInputException`. For example, JSON can encode a NUL in either path (`"Project\u0000.slnx"`); `Path.GetFullPath` throws. [ReviewConfigValidator.Validate](../../src/AiNetReview.Core/Configuration/ReviewConfigValidator.cs#L73) only translates `JsonException`, and [ReviewCommand](../../src/AiNetReview/Cli/ReviewCommand.cs#L150) only maps `InvalidReviewInputException` to `INVALID_INPUT`. The exception reaches the host-level fallback and is reported as `ANALYSIS_FAILED` (exit 3), although Epic 1 requires malformed configuration paths to be `INVALID_INPUT` (exit 2). The current path tests cover rooted, traversal, backslash, and symlink cases, but not invalid path characters or CLI classification.

### [P2] Performance test samples current memory instead of measuring peak private bytes

[InfrastructureLoadTests.cs](../../tests/AiNetReview.IntegrationTests/Performance/InfrastructureLoadTests.cs#L70) updates the reported peak from `Process.PrivateMemorySize64` every 10 ms (lines 71–88). This is a sample of current private memory and can miss short-lived peaks between polls; it does not provide the process's actual peak private bytes required by Epic 4. Consequently, the recorded 0.26 GiB result is not sufficient evidence that the 6 GiB peak limit was met, even though elapsed time, generated load, publication, and successful exit are checked. The release gate needs a source of peak usage or must describe and justify a measurement that captures it.

### [P2] Operational log events omit the command field

[HostLogging.Initialize](../../src/AiNetReview/HostLogging.cs#L28) enables `FromLogContext`, but only the startup event at line 41 carries `Command`. Subsequent start, cancellation, analysis-error, report-error, and completion events in [ReviewCommand](../../src/AiNetReview/Cli/ReviewCommand.cs#L76) do not attach that property; only completion includes `RunId` (line 138). Epic 1 specifies that log events include the command, run ID when available, and failure context. Existing process tests check that startup includes the command and that a successful run ID appears somewhere in the log, but do not assert the metadata on operational events.

## Evidence reviewed

- Roadmap reports successful build, FastTests, IntegrationTests, separate performance test, and self-analysis on Windows 10.0.26200.0 / .NET SDK 10.0.400.
- The performance implementation generates six C# projects and records the source line count and process result; the memory metric limitation is described above.
- The productive composition root adds only `TemplateNoOpRule`; the fixture rule is defined and registered by IntegrationTests.
- The report writer writes per-run temporary directories and atomically renames them after report files are closed; publication tests cover collisions, concurrency, previous-run preservation, and pre-publication failure cleanup.
- Loader and runner tests cover load/compile failures, source mutation after materialization, findings and evidence validation, cancellation, and rule exceptions.
