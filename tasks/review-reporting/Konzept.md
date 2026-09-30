---
status: ready
---

# Reliable entry points and readable Markdown reports

## Intention

AiNetReview produces Markdown files that people and review agents read directly before investigating the source code. Improve that existing workflow with exactly two changes: correct the compiler-entry-point type false positive and group the existing Markdown findings by project and source file.

The expected usefulness is sufficient to implement these changes. There is no measurement campaign, comparative repository audit, or required noise-reduction percentage. Automated correctness tests remain required. All product decisions below are final; this concept does not start the roadmap or implementation workflow.

## Verified starting point

The implementation references below were checked at `eb5b748a0b50af072278578c29c6389bed2cefbf`. Subsequent concept-only commits have not changed production behavior.

| Area | Current behavior and reference |
| --- | --- |
| Dead-code selection | [DeadCodeCandidatesAnalysis](../../src/AiNetReview.Core/ReviewAnalyses/DeadCodeCandidates/DeadCodeCandidatesAnalysis.cs) obtains `Compilation.GetEntryPoint`, but evaluates a grouped type candidate before protecting the entry-point method. The containing type is not protected by that compiler-entry-point check. |
| Findings | [FindingDraft](../../src/AiNetReview.Core/Findings/FindingDraft.cs) and [CurrentFindingValidator](../../src/AiNetReview.Core/Findings/CurrentFindingValidator.cs) define and validate the existing individual findings. |
| Review metadata | [ReviewRunner](../../src/AiNetReview.Core/Analysis/ReviewRunner.cs), [ReviewFindingBuilder](../../src/AiNetReview.Core/Analysis/ReviewFindingBuilder.cs), and [ReviewFinding](../../src/AiNetReview.Core/Analysis/ReviewFinding.cs) provide participating files, changed files, and symbol-based relationships. |
| Reports | [MarkdownReportWriter](../../src/AiNetReview.Core/Reporting/MarkdownReportWriter.cs) publishes a root index, two view indexes, and nonempty analysis reports using one temporary directory and final directory rename. |
| Presentation | [Current findings](../../docs/review/findings.md) describes compact signals, symbol identities, cluster members, forwarding paths, indirect test paths, and related-analysis indications. The existing Markdown deliberately omits complete raw metrics, long rationale, and snippets. |
| Host | [CLI](../../docs/interfaces/cli.md) defines the existing review/baseline commands, process response, error codes, and logging. The manual audit uses the same report writer. |

The entry-point-named test in [DeadCodeCandidatesAnalysisTests](../../tests/AiNetReview.FastTests/ReviewAnalyses/DeadCodeCandidatesAnalysisTests.cs) uses the shared library-project fixture and public API protection. It does not establish protection of an internal entry-point type in an executable. New regression fixtures must explicitly use executable compilation options.

## Scope

### Must

- Protect the actual compiler entry-point method and its containing type chain from dead-code candidacy in production executable projects under both existing `apiSurface` modes.
- Group each nonempty Markdown analysis report by project and representative source file, with a compact summary and exact counts. Keep every individual finding in that same report.
- Preserve the existing finding semantics, compact signals, represented members, relationships, view selection, file layout, and atomic publication.
- Keep the existing configuration, CLI response, baseline command, logging, and manual-audit behavior.
- Add focused correctness tests and update affected current-state documentation when the behavior is implemented.

### Not

- JSON findings exports, export schemas, new finding hashes/IDs, serialization DTOs, or consumers for hypothetical programs.
- An additional aggregate dump, separate detail files, agent briefings, synchronization between output formats, or new report formats. The existing Markdown files are the report.
- Dumping every internal data field into Markdown: complete metric dictionaries, long rationale, full evidence snippets, or source copies.
- New audit commands, MSBuild isolation, stronger read-only guarantees, configuration options, packages, hosted services, or DI registrations.
- New structural-duplication or presentation-layer analyses; changed thresholds or control-flow weights; discounts for switches or guard clauses.
- Changes to missing-test-evidence detection, categories, uncertainty, or static test-path semantics.
- Suppression, sampling, truncation of finding lists, confidence tiers, quality scores, automatic false-positive classification, or refactoring recommendations.
- Performance/usefulness measurements, comparison campaigns, or promised reduction percentages.

## Compiler-entry-point correction

For each production C# project, use its existing compilation and `GetEntryPoint(cancellationToken)`. Compare symbols with `SymbolEqualityComparer.Default` within that compilation.

If an entry point exists, protect its `ContainingType` and every enclosing `ContainingType` before the grouped type-candidate decision. The enclosing chain must be protected so a nested entry point cannot be represented as a dead outer type. Keep the existing protection of the actual entry-point method.

Continue applying the existing method-selection rules to ordinary methods of protected types. Unrelated unused methods remain candidates. Sharing a file or namespace with an entry point does not protect another type. Protection is independent of accessibility, names, framework attributes, project names, and `apiSurface`.

If `GetEntryPoint` returns `null`, introduce no new protection. A library method named `Main` remains ordinary library code. When startup configuration selects one of several executable entry-point candidates, only the actual entry point and its containing chain receive this additional protection.

Top-level statements retain their existing generated-declaration exclusions. If their compiler-generated entry point belongs to a type with explicit declarations, including an explicit `partial Program`, protect that type using the same symbol rule. Its unrelated explicit methods still follow the existing rules. Existing test-project, generated-source, public API, and indirect-use protection remains in force.

Increment the `dead-code-candidates` descriptor's `BehaviorVersion` from `1` to `2`. Other behavior versions and the configuration schema stay unchanged.

### Minimal reference cases

Executable project, compiled as a console application:

```csharp
internal static class Bootstrap
{
    public static int Main() => 0;
    private static void UnusedHelper() { }
}

internal sealed class UnusedType { }
```

In both API modes, `Bootstrap` and its actual entry point produce no dead-code finding. `UnusedHelper` remains a method candidate; `UnusedType` remains a type candidate.

Library project, compiled as a dynamically linked library:

```csharp
internal static class Bootstrap
{
    public static int Main() => 0;
}
```

With no references or indirect protection, `Bootstrap` remains a grouped type candidate. The method name does not protect it. These examples are test specifications, not product code or project-specific exceptions.

## Markdown report contract

Keep the existing layout: root `index.md`, `changed-files/index.md`, `all-findings/index.md`, and `<analysisId>.md` in each view only when that analysis has findings in that view. Empty successful runs still contain exactly the three indexes. No additional report files are introduced.

Every nonempty analysis report has this section order:

1. Existing title, purpose, effective options, and review questions, including the static-test-path limitation notice for `missing-test-evidence-candidates`.
2. `## Summary`: the total number of findings in this analysis/view, then a table with columns `Project`, `Source file`, and `Findings`.
3. `## Findings`: all individual items grouped under `### Project: <projectPath>` and `#### File: <sourcePath> (<count> findings)`.

Group by the exact pair `(Finding.ProjectPath, Finding.SourcePath)`. The source is the finding's representative file. Group unconditionally, including a single finding; there is no threshold or configuration switch. Sort projects and files ordinally, then findings within each group by `StartLine`, `SubjectId`, and `Discriminator`, with ordinal string comparison. The summary uses the same group order.

Each finding contributes exactly one to its group and analysis/view total. Cluster members, partial declarations, and forwarding-path evidence in other files do not create additional group entries or counts. Their locations remain in the individual item's existing member/path list. The same source path in different projects belongs to separate groups.

The file heading supplies the representative path, so remove its repetition from an ordinary item's label. Keep the subject DocId, existing compact signal, category, and related-analysis indication. Project, file, analysis, subject, and the existing category/represented members identify the item; do not generate another identifier. Distinct findings at the same subject remain separate items.

Keep the existing special rendering: cluster member paths/DocIds, forwarding-path members in stored evidence call order, and the shortest indirect test path. Group counts never replace the individual items or imply that an entire containing class is untested. Continue omitting complete raw metrics, long rationale, and snippets. Agents use the reported symbols and paths to inspect the source.

Escape headings, table cells, code spans, item content, and index links correctly for Markdown, including pipes, backticks, brackets, Unicode, and newlines. Keep UTF-8 without BOM and LF line endings. Do not add source hyperlinks, HTML anchors, priority rankings, or inferred action items.

The root and view indexes retain their existing per-analysis counts, links, empty-state messages, baseline command, and review guidance. `changed-files` stays the primary working set; `all-findings` remains the complete reference view for an explicitly requested full audit. No findings are removed from the complete view. Working-view files remain independent editable copies.

## Selection and publication

Use the existing report writer and its review metadata. Do not add a shared export projection, new invariant-validation subsystem, or another writer. Preserve the current compatibility fallback for caller-created `ReviewRunResult` values without enriched `Findings`: derive participating paths from the representative source and evidence, treat them as changed, and invent no relationships.

The existing changed-view predicate is authoritative:

- For `missing-test-evidence-candidates`, include findings when `HasCSharpSnapshotChanges != false`. Without a baseline it shows all current findings; with a baseline, any added/changed/deleted C# snapshot path selects all its findings, and an unchanged C# snapshot selects none. Non-C# changes alone do not select it.
- For every other analysis, include a finding when `ChangedSourcePaths.Count > 0`.
- `all-findings` always contains every current finding.

A missing-test-evidence finding can be selected even if its own participating files are unchanged. Group only after selection and independently in each view. Existing relationships use project and symbol identity; sharing a file creates no new relationship. Retain the current related-analysis indication, including the `all-findings` suffix for a target outside the current view, without copying that target's details into the working view.

Grouping changes presentation only. Do not rerun analyses or reread source files during formatting. Keep existing publication hooks, final directory rename, run-ID collision handling, cancellation, Windows lock retries, and best-effort cleanup of the owned temporary directory. Persistent external locks can still leave the documented temporary directory. Preserve older runs and the baseline.

CLI commands, process responses, error messages/codes, logging, and `PublishedReport` remain unchanged. `baseline` still creates no report. The manual audit receives the same grouped Markdown through the existing writer, with its current external output and baseline command. Previous published runs are not rewritten.

## Verification and acceptance

Tests verify correctness; they do not measure usefulness, noise reduction, performance, or coverage.

- Reproduce the internal executable-entry-point type defect with a failing automated regression before fixing it. Verify both API modes, synchronous/asynchronous entry points, a non-`Program` startup type, nested/partial containing types, top-level statements, and an explicit `partial Program`.
- Verify unused ordinary methods in protected types and unrelated types still become candidates. Verify a library's `Main` remains unprotected and selecting one of multiple startup types protects only the actual entry-point chain. Retain existing public API, indirect-use, uncertainty, generated-source, and test-project tests.
- Verify summary/group/analysis counts for one and many findings, the same source in different projects, partial declarations, cross-file/cross-project clusters, and forwarding paths. Every original finding appears once within its analysis/view; represented members are not double-counted.
- Verify subjects, signals, categories, related-analysis indications, cluster members, forwarding order, indirect-test paths, and Markdown escaping remain readable and complete under the presentation contract.
- Verify no baseline, unchanged baseline, changes to non-representative members, and added/changed/deleted C# snapshot paths. Include missing-test-evidence findings whose own files are unchanged and the non-C#-only case.
- Verify empty views, active analyses with no findings, all analyses disabled, omission of empty analysis files, and the unchanged three-index empty-run layout.
- Retain publication/host coverage for concurrent runs, older-run preservation, failed/cancelled publication, normal success responses, and no report for `baseline`. Verify editing working Markdown does not affect the complete view.
- Cover centrally hosted report output with an isolated fixture using the existing mechanisms. No additional audit of a real external repository is an acceptance prerequisite.

Run the build, FastTests, and normal IntegrationTests gates from [Build and Tests](../../docs/development/build-and-tests.md) during implementation. Do not add performance or comparative-audit gates. Update layout assertions for the grouping while retaining meaningful selection and failure assertions.

When implemented, update [Current findings](../../docs/review/findings.md) as the primary report contract and affected README/build-test descriptions. Current-state documentation remains unchanged during concept planning. No configuration or registry changes are required.

Acceptance is the corrected entry-point protection plus readable grouped Markdown with every individual finding retained, existing selection/publication behavior preserved, and the required correctness tests passing.
