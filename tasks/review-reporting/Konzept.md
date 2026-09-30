---
status: ready
---

# Reliable entry points and complete review reports

## Intention

AiNetReview should give people and review agents a manageable overview of its findings while retaining the complete evidence needed to investigate each item. This change corrects the compiler-entry-point type false positive, groups Markdown findings by project and source file, and adds a versioned JSON export of the existing analysis results and relationships.

The implementation decision is **yes, with exactly these three changes**. Their expected usefulness justifies implementation without a measurement campaign, comparative repository audits, precision studies, or a promised percentage reduction. Automated correctness tests remain required. This is a finalized implementation contract; it contains no unresolved product decisions and does not start the roadmap or implementation workflow.

## Verified starting point

The following statements were checked against repository code and tests at `eb5b748a0b50af072278578c29c6389bed2cefbf`. Links point to the implementation to extend, rather than to copies of external repositories.

| Area | Current behavior and reference |
| --- | --- |
| Dead-code selection | [DeadCodeCandidatesAnalysis](../../src/AiNetReview.Core/ReviewAnalyses/DeadCodeCandidates/DeadCodeCandidatesAnalysis.cs) obtains `Compilation.GetEntryPoint`, but checks a type candidate before checking its methods. Compiler entry-point protection applies to the method, not to the earlier type decision. |
| Current findings | [FindingDraft](../../src/AiNetReview.Core/Findings/FindingDraft.cs) carries the project, representative source, subject, discriminator, line, rationale, numeric metrics, evidence, and represented symbols. [CurrentFindingValidator](../../src/AiNetReview.Core/Findings/CurrentFindingValidator.cs) validates the source snapshot and unique identity tuple. |
| Review metadata | [ReviewRunner](../../src/AiNetReview.Core/Analysis/ReviewRunner.cs), [ReviewFindingBuilder](../../src/AiNetReview.Core/Analysis/ReviewFindingBuilder.cs), and [ReviewFinding](../../src/AiNetReview.Core/Analysis/ReviewFinding.cs) retain participating files, changed participating files, and precise relationships through project and symbol identity. |
| Selection | [MarkdownReportWriter](../../src/AiNetReview.Core/Reporting/MarkdownReportWriter.cs) selects `missing-test-evidence-candidates` using `HasCSharpSnapshotChanges`; other analyses use changed participating source files. |
| Publication | The same writer publishes the root index and both Markdown views through one temporary directory and a final directory rename. It has a compatibility fallback for caller-created `ReviewRunResult` values without enriched `Findings`. There is no JSON findings export. |
| Markdown | [Current findings](../../docs/review/findings.md) and the writer define compact per-finding signals, purpose, effective options, review questions, represented cluster members, and the ordered forwarding path. Raw rationale, complete metrics, and snippets are not printed in Markdown. |
| Host | [CLI contract](../../docs/interfaces/cli.md) defines `review` and `baseline`, one compact success JSON line, and the existing error/exit-code mapping. The manual audit also uses the report writer. |

The current entry-point-named test in [DeadCodeCandidatesAnalysisTests](../../tests/AiNetReview.FastTests/ReviewAnalyses/DeadCodeCandidatesAnalysisTests.cs) uses the shared library-project fixture and public API protection. It does not establish the required protection of an internal entry-point type in an executable. The new regression tests must explicitly use executable compilation options.

## Scope

### Must

- Protect the actual compiler entry-point method and its containing type chain from dead-code candidacy in every production executable project, under both existing `apiSurface` modes.
- Apply unconditional project/file grouping and exact finding counts to every nonempty Markdown analysis report in both existing views, with all individual items still available in that report.
- Publish `changed-files/findings.json` and `all-findings/findings.json` for every successful review, including empty reviews and reviews with all analyses disabled.
- Preserve the existing finding data, evidence order, represented symbols, relationships, and view selection. Give every finding an unambiguous deterministic ID shared by JSON and Markdown.
- Produce both JSON files from the same prepared report data as Markdown and publish the whole report set atomically.
- Keep the existing CLI, baseline, configuration, manual-audit, logging, and review-guidance contracts except for the specified report additions and dead-code correction.
- Add correctness and failure tests and update the affected current-state documentation when implementation changes the product.

### Not

- A new `audit` command, changes to external-output validation, MSBuild isolation, or a new read-only guarantee.
- New structural-duplication or presentation-layer analyses; changes to existing duplication thresholds or control-flow counting; discounts for switches or guard clauses.
- Changes to missing-test-evidence detection, thresholds, categories, uncertainty handling, or static test-path semantics.
- Suppression, sampling, truncation, confidence tiers, severity ranking, quality scores, inferred architectural clusters, automatic false-positive classification, or refactoring recommendations.
- New command-line options, report-format switches, configuration fields, packages, hosted services, DI registrations, dynamic analysis loading, or dependencies from Core to the host or MCP.
- Source-file copies, complete source text, source snapshot hashes, runtime coverage, historical comparisons of finding sets, or a separate agent briefing/prompt file.
- Performance or usefulness measurements, real-repository comparison campaigns, new performance acceptance thresholds, or claims about percentage reductions.

## Compiler-entry-point correction

For each production C# project, use that project's existing compilation and `GetEntryPoint(cancellationToken)`. Symbol comparisons use `SymbolEqualityComparer.Default` within that compilation.

If there is an entry point, protect its `ContainingType` and every enclosing `ContainingType` from being emitted as a `type-candidate`. Protecting the enclosing chain prevents a nested entry point from being represented by a dead outer type. This rule runs before the grouped type-candidate decision. Continue examining ordinary methods of those protected types through the existing method-selection rules: unrelated unused methods remain candidates. Keep the existing protection of the actual entry-point method.

The rule is independent of accessibility, class/method names, namespace, project name, framework attributes, and `apiSurface`. It does not protect sibling or nested types merely because they share the entry-point file. If `GetEntryPoint` returns `null`, introduce no new protection. A library method named `Main` is ordinary library code. If multiple `Main` methods exist and the executable selects one through its startup configuration, only the actual entry point and its enclosing chain receive this additional protection.

Top-level statements need no explicit declaration finding for their compiler-generated method or type. If a generated entry point belongs to a type that also has explicit declarations, protect that containing type by the same symbol rule; unrelated explicit methods still follow the existing rules. Existing generated-source and test-project exclusions remain in force.

Increment the `dead-code-candidates` descriptor's `BehaviorVersion` from `1` to `2` because its selection behavior changes. Other analysis behavior versions and the configuration schema remain unchanged.

### Minimal reference cases

These examples are test specifications, not product code or special rules for a particular repository. They replace the need to retain the previous external reference-code tree.

Executable project, compiled as a console application:

```csharp
internal static class Bootstrap
{
    public static int Main() => 0;
    private static void UnusedHelper() { }
}

internal sealed class UnusedType { }
```

In both API modes, `Bootstrap` and its actual entry-point method produce no dead-code finding. `UnusedHelper` remains a method candidate and `UnusedType` remains a type candidate. This verifies that the correction does not exempt an entire executable or every member of its startup type.

Library project, compiled as a dynamically linked library:

```csharp
internal static class Bootstrap
{
    public static int Main() => 0;
}
```

With no references or indirect protection, `Bootstrap` remains a grouped type candidate. Its method name creates no compiler-entry-point protection.

## One report projection and existing view semantics

Prepare the report's complete finding collection, finding IDs, changed-view membership, relationships, and group counts once in Core's reporting layer. Markdown and JSON consume that same immutable projection. Do not rerun analyses, discover source files, read changed source text, or reconstruct semantic relationships during formatting.

Use enriched `ReviewRunResult.Findings` whenever present. Preserve the writer's existing fallback when that collection is empty but analysis results contain findings: derive participating paths from the representative source and evidence, treat those paths as changed, and provide no invented relationships. Both formats use the same fallback. A genuinely empty result produces empty collections. Existing result/configured-analysis consistency checks remain applicable.

The prepared complete collection must correspond one-to-one by identity tuple to `result.Analyses` and retain each draft's data. Its count must equal `result.DetectedCount`. Reject duplicate identities or an incomplete/inconsistent enriched collection as a report failure. This validates reporting invariants, not source code again; source-snapshot validation remains the runner's responsibility.

The authoritative changed-view predicate remains:

- For `missing-test-evidence-candidates`, include the finding when `HasCSharpSnapshotChanges != false`. `null` means no baseline comparison was supplied; `true` means any C# snapshot path was added, changed, or deleted; `false` means the C# snapshot is unchanged. Non-C# changes alone do not select this analysis.
- For every other analysis, include the finding when `ChangedSourcePaths.Count > 0`.
- `all-findings` includes every current validated finding. With no baseline, production runs select every current finding in both views.

Do not infer view membership merely from `ChangedSourcePaths`: a missing-test-evidence finding can belong to the changed view even when its own participating files are unchanged. Every exported finding therefore has an explicit `includedInChangedFiles` Boolean computed by the authoritative predicate.

Cross-analysis relationships retain their existing project-and-symbol matching, including the precise participating symbol in a cluster. Sharing only a file is not a relationship. A reference to a finding outside the current view is retained as a reference, with `targetView: "all-findings"`; its finding payload is not copied into the changed view. References within the current view use that view's name. A related target must resolve to a finding in the same run's complete projection. An unresolved target or an identity collision fails report publication instead of silently dropping data.

## Finding identity

The identity tuple is exactly the validator's current tuple, in this order:

```text
(analysisId, projectPath, sourcePath, subjectId, discriminator)
```

Derive `findingId` as `f-` plus the 64 lowercase hexadecimal characters of SHA-256 over the concatenation of the five encoded fields. Encode each field as its UTF-8 bytes preceded by the byte length as an unsigned 32-bit big-endian integer. This length-prefix encoding distinguishes separators and arbitrary Unicode without delimiter ambiguities. Use the exact validated strings; do not change path casing, Unicode normalization, or symbol identity.

Run ID, view, line numbers, metrics, evidence, baseline state, and group position are excluded. The same tuple gets the same ID across views and repeated runs. Moving a declaration to another representative source, changing its subject identity, or changing its discriminator changes its ID. This is identity stability for an unchanged tuple, not a promise to track renames or moves. If two different tuples ever produce the same ID, fail publication.

Markdown prints each item's full ID once alongside its subject or cluster/path label. Together with the report path and group context, this makes every finding independently addressable and directly mappable to JSON without introducing source hyperlinks or HTML anchors.

## Markdown contract

Retain the root and view indexes and existing `<analysisId>.md` filenames. Do not create separate detail files. A nonempty analysis report has this section order:

1. Existing title, purpose, effective options, and review questions, including the static-test-path limitation notice for `missing-test-evidence-candidates`.
2. `## Summary` with the total number of findings in this analysis and view, followed by a table with columns `Project`, `Source file`, and `Findings`.
3. `## Findings`, grouped under `### Project: <projectPath>` and `#### File: <sourcePath> (<count> findings)` headings, containing every original individual finding item and its finding ID.

Group by the exact pair `(Finding.ProjectPath, Finding.SourcePath)`, where the source is the representative source of the finding. Apply grouping for every count, including a single finding; no size threshold or configuration choice is introduced. Sort projects and representative files ordinally. Within each group sort by `StartLine`, `SubjectId`, and `Discriminator` using ordinal string comparison. The summary table follows the same group order.

Each finding contributes exactly one to its representative group and to its analysis/view total. Multi-file clusters, partial types, and forwarding paths do not contribute a second count for an evidence file or represented member. Their complete member list remains in the individual finding detail and in JSON. The same source path in two projects is two separate groups.

The file heading supplies the representative path, so an ordinary item's repeated representative path is removed from its item label. Keep its subject DocId, existing signal, and related-analysis indication. Keep all existing special rendering: cluster member paths/DocIds, forwarding-path members in evidence call order, and the shortest indirect test path. Do not replace an individual finding with a class-level inference or a count-only entry.

Grouping is presentation only. It changes neither finding identities nor selections, metrics, category labels, or review semantics. Markdown continues to omit complete raw metrics, long rationale, and snippets; JSON supplies them. Escape group headings, table cells, item content, code spans, and index links using appropriate Markdown escaping, including pipes, backticks, brackets, Unicode, and newlines.

Indexes retain their current per-analysis counts and links, empty-state distinctions, baseline command, and review guidance. The root index adds a primary link to `changed-files/findings.json` in its changed-files section, and a reference link to `all-findings/findings.json` only in its reference section. Each view index links its own `findings.json`, even when that view is empty. Do not embed all-findings details or file-group summaries in the root index.

The existing direction to use `changed-files` as the primary working set and to inspect `all-findings` only for an explicitly requested full audit applies equally to the new JSON files. Editing the working-view Markdown does not alter the complete view or either JSON file. JSON is the original run export, not a synchronized record of later manual Markdown edits; state this in the index guidance.

## JSON contract: schema version 1

Every published run contains:

```text
<runId>/
  index.md
  changed-files/
    index.md
    findings.json
    <analysisId>.md       # only for analyses with findings in this view
  all-findings/
    index.md
    findings.json
    <analysisId>.md       # only for analyses with findings in this view
```

Each JSON file is a complete object for its own view, with the fields below. All listed fields are required. Collections and objects are emitted even when empty; `hasCSharpSnapshotChanges` is the only nullable field. No additional schema file is published. The tables below are the schema contract and must be transferred into the current-state reporting documentation when implemented.

### Root object

| Field | Type | Meaning |
| --- | --- | --- |
| `schemaVersion` | integer | Exactly `1`; independent of analysis behavior versions and the configuration schema. |
| `runId` | string | The enclosing published run directory's existing run ID. |
| `view` | string enum | Exactly `changed-files` or `all-findings`. |
| `reviewPolicy` | string enum | `primary-working-set` for changed-files; `full-audit-only` for all-findings. |
| `projectRoot` | string | The validated absolute target project root, with `/` path separators. |
| `solutionPath` | string | The validated solution path relative to that root, with `/` separators. |
| `hasCSharpSnapshotChanges` | Boolean or null | The runner value, with the semantics defined above. |
| `findingCount` | nonnegative integer | Number of finding objects in this view; grouping does not change it. |
| `analyses` | analysis array | Exactly the configured active analyses, including those with zero findings in this view. Disabled analyses are absent. |
| `groups` | group array | Nonempty representative project/file groups within each analysis and this view. |
| `findings` | finding array | Exactly the view's selected findings, one object per identity. |

### Analysis object

| Field | Type | Meaning |
| --- | --- | --- |
| `analysisId` | string | Registered descriptor ID. |
| `title` | string | Descriptor title. |
| `behaviorVersion` | positive integer | Descriptor behavior version. |
| `purpose` | string | Descriptor purpose. |
| `measurement` | string | Descriptor description of the signal and its interpretation limits. |
| `reviewQuestions` | string array | Descriptor questions, in descriptor order. |
| `effectiveOptions` | object | Actual resolved option values as JSON values; preserve their JSON types and values. |
| `findingCount` | nonnegative integer | This analysis's selected count in this view. |

### Group object

Fields, in order: `analysisId` (string), `projectPath` (string), `sourcePath` (string), `findingCount` (positive integer), and `findingIds` (string array).

Groups use the same representative ownership and counts as Markdown. `findingIds` has exactly `findingCount` unique IDs, in within-group finding order. Every finding belongs to exactly one group in each export containing it. The sum of group counts equals the root finding count; per-analysis sums equal analysis counts. No empty groups or inferred containing-class groups are emitted.

### Finding object

| Field | Type | Meaning |
| --- | --- | --- |
| `findingId` | string | The deterministic ID defined above. |
| `analysisId` | string | Owning analysis. |
| `projectPath` | string | Validated representative project's root-relative path. |
| `sourcePath` | string | Validated representative source's root-relative path. |
| `subjectId` | string | Exact analysis subject identity. |
| `discriminator` | string | Exact analysis discriminator/category. |
| `startLine` | positive integer | One-based representative location in the loaded snapshot. |
| `rationale` | string | Complete validated rationale. |
| `metrics` | object | All actual named finite numeric metric values, without rounding or derived scores. |
| `evidence` | evidence array | All validated evidence, in stored order. |
| `relatedSymbols` | symbol array | All explicitly represented symbols, including cluster members. |
| `sourcePaths` | string array | All participating source paths from the prepared review metadata. |
| `changedSourcePaths` | string array | Actual changed participating paths; can be empty for a selected missing-test-evidence finding. |
| `includedInChangedFiles` | Boolean | Result of the authoritative changed-view predicate. |
| `relatedFindings` | reference array | All existing precise finding relationships, including references outside the current view. |

An evidence object has `sourcePath` (string), `line` (positive integer), `label` (string), `detail` (string), and `snippet` (string), in that order. Export exact loaded-snapshot values; do not truncate, fetch the current disk version, or reorder evidence. Evidence order carries meaning for forwarding chains and indirect test paths.

A symbol object has `projectPath` (string), `sourcePath` (string), `symbolId` (string), and `line` (positive integer), in that order. These are the stored `FindingSymbol` fields. Preserve identities of all members even when they belong to another project or non-representative file.

A reference object has these fields, in order: `findingId`, `targetView`, `analysisId`, `projectPath`, `sourcePath`, `subjectId`, `discriminator`, `symbolSourcePath`, `symbolId`, and `symbolLine`. All are strings except the positive integer `symbolLine`; `targetView` uses the root `view` enum. The target tuple and shared-symbol location map exactly to `ReviewFindingReference`. Multiple references to one target through different represented symbols remain separate. This is factual relationship data, not authorization to process the reference-only view.

All relative paths refer to the target `projectRoot`, not to the output directory or current working directory. Forward slashes are used. Generated intermediate evidence retains the already validated logical source path; the export does not promise that every generated path is a physical file. No absolute source links, OS handles, source hashes, timestamps beyond the existing run ID, or user-specific output paths are added to finding identities.

### Ordering and encoding

- JSON uses camelCase field names and the property order specified above; use explicit export DTOs or equivalent explicit serialization, not unrestricted serialization of internal domain objects.
- Root analyses are ordered by ordinal `analysisId`. Groups are ordered by ordinal `(analysisId, projectPath, sourcePath)`. Findings are ordered by `(analysisId, projectPath, sourcePath, startLine, subjectId, discriminator)`, with ordinal comparison for strings.
- Symbol arrays are ordered by `(projectPath, sourcePath, symbolId, line)`. Source-path arrays are ordered ordinally, retaining the distinct set prepared by the existing path-comparison rules. References are ordered by `(analysisId, projectPath, sourcePath, subjectId, discriminator, symbolSourcePath, symbolId, symbolLine)`.
- Evidence and review-question arrays retain stored order. Array-valued effective options retain their order. Object keys in metrics and effective options, including nested option objects, are ordered ordinally.
- Use `System.Text.Json`, UTF-8 without BOM, two-space indentation, LF line endings, one terminal LF, invariant finite number serialization, and its default safe string escaping. Snippets and strings round-trip exactly after JSON parsing. Do not stringify numbers or use culture-dependent formatting.
- Identical prepared input, configuration, run ID, and view produce identical bytes. Different runs have different run IDs; comparing them is not a byte-identity guarantee. Across views the same finding retains the same ID and raw data; only relationship `targetView` can change with visibility.
- Consumers reject unsupported `schemaVersion` values and can ignore unknown added properties within version 1. Removing fields, changing their types or meanings, or changing identity encoding requires a new schema version. This scope introduces no consumer or schema-migration tool.

## Publication, failures, and compatibility

Extend the existing Core report publication operation; do not add an independently published JSON writer. A focused JSON formatter and the common report projection are reporting responsibilities. The host continues to invoke `MarkdownReportWriter.WriteAsync`; preserve its public call shape and `PublishedReport` fields.

Write both JSON files, both view indexes, the nonempty analysis reports, and the root index inside the same owned temporary run directory before the existing publication hook and final rename. A published run is complete in both formats. JSON serialization, invariant validation, or write failures fail the entire report, using `REPORT_FAILED` and exit code `4` through the existing host mapping. The host publication error message becomes `Review report could not be published.` and its log message becomes `Review report could not be published`, covering both formats. Analysis failures remain `ANALYSIS_FAILED`; cancellation remains `CANCELLED` with exit code `130`.

Retain cancellation checks, collision retries with a new run ID, Windows lock retries, and best-effort cleanup of only the owned temporary directory. When rerendering after a run-ID collision, regenerate both formats with the new ID. Do not claim guaranteed temporary cleanup under persistent external locks; retain the documented temporary-directory limitation. Previous published runs and the baseline are untouched by a failed report.

The CLI success response remains exactly its existing shape: `status`, `runId`, `indexPath`, and `counts.detected`. Discover JSON through the root/view indexes or the fixed paths relative to the returned run's index. Findings are never emitted on stdout, and logging stays executable-relative. The `baseline` command still publishes no report and no findings JSON.

The existing manual audit receives these additions through the shared report writer, including its external output path, target-root metadata, and existing baseline command. This creates no stronger target-repository write guarantee. Existing Markdown files remain independently editable copies. There are no automatic edits or migrations of previously published runs.

## Verification and acceptance

Verification establishes the specified behavior. It does not measure usefulness, noise reduction, runtime coverage, or performance.

### Entry-point regression

- First reproduce the internal executable-entry-point type finding with a failing automated test; then verify the correction under both API modes.
- Cover synchronous and asynchronous explicit entry points, a non-`Program` type, nested containing types, partial startup types, ordinary top-level statements, and top-level statements combined with an explicit `partial Program` declaration.
- Verify unused ordinary methods in a protected type and unrelated types in the same project/file still become candidates.
- Verify a library's `Main` is not specially protected, and selecting one of multiple executable startup types protects only the actual entry point and enclosing chain.
- Keep existing public API, indirect-use, uncertainty, test-project, and generated-source protection tests passing. These are focused fixtures, not comparative audits of real repositories.

### Projection, identity, and presentation

- Verify exact tuple encoding with a fixed SHA-256 test vector; identical tuples across views/runs retain IDs, line-only changes retain IDs, identity-field changes produce different IDs, and delimiter-like/Unicode fields are unambiguous.
- Verify representative grouping and totals for one item, many items, different projects sharing a source path, partial declarations, cross-file/cross-project clusters, and forwarding paths.
- Verify every original item remains in Markdown details with its ID and correct category/signal, and is present once in JSON and once in its group. Verify no unrelated same-file relationship is invented.
- Verify escaping for Markdown headings/tables/items and JSON round trips with special characters and multiline strings; preserve evidence call order and indirect-test-path order.
- Verify compatibility fallback results use the same findings and membership in both formats, without fabricated relationships. Verify incomplete enriched collections and duplicate identities fail publication.

### Export and selection

- Check all required fields and JSON types, complete rationale/metrics/evidence/symbols, finite numeric values, analysis versions/options, root/group/analysis totals, valid target references, and deterministic serialization for a fixed run ID.
- Check no baseline, unchanged baseline, changed non-representative member, and added/changed/deleted C# snapshot paths. Include the missing-test-evidence case selected despite empty `changedSourcePaths`, and the non-C#-only change case.
- Check both empty-view exports, empty active analyses, and all analyses disabled. Existing empty Markdown analysis-file omission remains valid; successful empty runs now have five files rather than three.
- Check changed-view references to complete-view targets without copying their payloads, plus the full-audit-only guidance and discoverable JSON links. Manual edits to working Markdown must not mutate reference Markdown or JSON.

### Publication and host

- Extend existing report-publication tests to check JSON completeness in successful concurrent runs, preserved older runs, retries, and failed/cancelled publication.
- Exercise a deterministic JSON write/serialization-stage failure before rename; assert no final run and no success stdout, with existing temporary-cleanup semantics. Test identity/reference invariant rejection separately. Use internal test seams where necessary; do not introduce a production failure option.
- Through the production host, verify successful JSON publication, unchanged success response/counts, `REPORT_FAILED` for a JSON publication failure, and no report/export for `baseline`, failed analysis, or cancellation before publication.
- Cover the centrally hosted manual-audit report path with an isolated fixture using the existing writer/configuration mechanisms; do not launch an audit of a real external repository as an acceptance prerequisite.

For implementation, run the repository's build, FastTests, and normal IntegrationTests gates from [Build and Tests](../../docs/development/build-and-tests.md). Do not run the separate performance or real-repository audit scripts as a gate for this work. Update report-layout/file-count assertions to the new contract while retaining meaningful selection and failure assertions.

Update [Current findings](../../docs/review/findings.md) as the primary report/schema contract, [CLI](../../docs/interfaces/cli.md) for publication and export discovery, and the affected README, architecture, and build/test descriptions when the feature exists. Configuration defaults and the production analysis registry require no changes. Until implementation, current-state docs remain unchanged.

Acceptance is the entry-point correction plus complete, consistently grouped and atomically published Markdown/JSON reports under these contracts, with required correctness tests passing. It is not contingent on a measured benefit.
