---
status: ready
---

# Structural duplication, reliable entry points, and readable Markdown reports

## Intention

AiNetReview produces Markdown files that people and review agents read directly before investigating the source code. Improve that workflow with exactly three changes: detect repeated structural code fragments despite renamed local variables or parameters, correct the compiler-entry-point type false positive, and group the Markdown findings by project and source file. Structural duplication adds a new review signal; the other changes improve correctness and readability.

The expected usefulness is sufficient to implement these changes. There is no measurement campaign, comparative repository audit, or required noise-reduction percentage. Automated correctness tests remain required. All product decisions below are final; this concept does not start the roadmap or implementation workflow.

## Verified starting point

The implementation references below were checked at `eb5b748a0b50af072278578c29c6389bed2cefbf`. Subsequent concept-only commits have not changed production behavior.

| Area | Current behavior and reference |
| --- | --- |
| Dead-code selection | [DeadCodeCandidatesAnalysis](../../src/AiNetReview.Core/ReviewAnalyses/DeadCodeCandidates/DeadCodeCandidatesAnalysis.cs) obtains `Compilation.GetEntryPoint`, but evaluates a grouped type candidate before protecting the entry-point method. The containing type is not protected by that compiler-entry-point check. |
| Findings | [FindingDraft](../../src/AiNetReview.Core/Findings/FindingDraft.cs) and [CurrentFindingValidator](../../src/AiNetReview.Core/Findings/CurrentFindingValidator.cs) define and validate the existing individual findings. |
| Review metadata | [ReviewRunner](../../src/AiNetReview.Core/Analysis/ReviewRunner.cs), [ReviewFindingBuilder](../../src/AiNetReview.Core/Analysis/ReviewFindingBuilder.cs), and [ReviewFinding](../../src/AiNetReview.Core/Analysis/ReviewFinding.cs) provide participating files, changed files, and symbol-based relationships. |
| Existing duplication | [DuplicateCodeDetector](../../src/AiNetReview.Core/Analysis/DuplicateCodeDetector.cs) compares whole executable bodies using distinct five-token n-grams, retaining identifier and literal text. [DuplicateCodeCandidatesAnalysis](../../src/AiNetReview.Core/ReviewAnalyses/DuplicateCodeCandidates/DuplicateCodeCandidatesAnalysis.cs) reports connected similarity clusters with configurable token/similarity thresholds. It does not normalize bound local names or compare statement fragments independently of their containing body. |
| Reports | [MarkdownReportWriter](../../src/AiNetReview.Core/Reporting/MarkdownReportWriter.cs) publishes a root index, two view indexes, and nonempty analysis reports using one temporary directory and final directory rename. |
| Presentation | [Current findings](../../docs/review/findings.md) describes compact signals, symbol identities, cluster members, forwarding paths, indirect test paths, and related-analysis indications. The existing Markdown deliberately omits complete raw metrics, long rationale, and snippets. |
| Host | [CLI](../../docs/interfaces/cli.md) defines the existing review/baseline commands, process response, error codes, and logging. The manual audit uses the same report writer. |

The entry-point-named test in [DeadCodeCandidatesAnalysisTests](../../tests/AiNetReview.FastTests/ReviewAnalyses/DeadCodeCandidatesAnalysisTests.cs) uses the shared library-project fixture and public API protection. It does not establish protection of an internal entry-point type in an executable. New regression fixtures must explicitly use executable compilation options.

## Scope

### Must

- Protect the actual compiler entry-point method and its containing type chain from dead-code candidacy in production executable projects under both existing `apiSurface` modes.
- Add the production analysis `structural-duplication-candidates` with the fragment and normalization contract below, registered through the existing static registry and configured with the existing `enabled` mechanism.
- Group each nonempty Markdown analysis report by project and representative source file, with a compact summary and exact counts. Keep every individual finding in that same report.
- Preserve the existing finding semantics, compact signals, represented members, relationships, view selection, file layout, and atomic publication.
- Keep the configuration schema, existing analysis options, CLI response, baseline command, logging, and manual-audit behavior. Register and document the new analysis, include it in generated defaults, and list it in the repository-root configuration.
- Add focused correctness tests and update affected current-state documentation when the behavior is implemented.

### Not

- JSON findings exports, export schemas, additional finding hashes/IDs beyond the existing subject/discriminator model, serialization DTOs, or consumers for hypothetical programs.
- An additional aggregate dump, separate detail files, agent briefings, synchronization between output formats, or new report formats. The existing Markdown files are the report.
- Dumping every internal data field into Markdown: complete metric dictionaries, long rationale, full evidence snippets, or source copies.
- New audit commands, MSBuild isolation, stronger read-only guarantees, custom configuration options, packages, hosted services, or DI registrations beyond the new analysis's normal registration.
- Presentation-layer analyses, changed existing duplication thresholds or control-flow weights, and discounts for switches or guard clauses.
- Fuzzy structural similarity, API-vector matching, semantic-equivalence claims, arbitrary type/member/literal abstraction, edit-distance matching, or tolerating reordered/inserted/deleted statements within a matched fragment.
- Changes to missing-test-evidence detection, categories, uncertainty, or static test-path semantics.
- Suppression of existing analysis results, sampling/truncation of selected finding lists, confidence tiers, quality scores, automatic false-positive classification, or refactoring recommendations. The new analysis's explicit size/containment selection is part of detection, not post-publication suppression.
- Performance/usefulness measurements, comparison campaigns, or promised reduction percentages.

## Structural duplication analysis

### Signal and configuration

Add `structural-duplication-candidates`, titled `Structural Duplication Candidates`, with `BehaviorVersion: 1` and default enabled. Its only configuration field is the existing Boolean `enabled`; it has no custom thresholds or similarity modes. Explicitly register it as `IReviewAnalysis` in the existing [composition root](../../src/AiNetReview/Bootstrap/ServiceRegistration.cs). Generated default configuration includes it, and the repository-root `ainetreview.json` lists it as enabled. Existing configurations that do not list it continue to run only their configured analyses; no existing user configuration is automatically migrated.

The analysis reports groups of repeated statement fragments with identical normalized syntax, including cases with different original local/parameter names. It finds fragments inside otherwise different executable bodies. It does not claim semantic equivalence, a defect, or that extraction is appropriate. The review question is: `Is this repeated structure intentional, or would a shared implementation improve maintenance without hiding meaningful differences?`

Keep `duplicate-code-candidates` and its options/behavior unchanged. That analysis detects token-similar whole bodies; the new analysis detects exact structural matches after local-name normalization in statement fragments. Do not suppress one analysis's findings based on the other. Some candidates can appear in both analyses; existing symbol-based relationships show that connection. Do not sum these counts as distinct defects.

### Eligible source and fragments

- Work only on loaded production C# documents, using the existing test-project/generated-source/generated-symbol classifiers and checked root-relative paths. Comparisons can span files and production projects in the same solution.
- Eligible owners are explicit ordinary methods, instance/static constructors, property/event accessors, and local functions with block bodies, matching the existing duplication analysis's supported method kinds. Expression-only bodies, lambdas/anonymous methods, top-level statements, operators, destructors, field initializers, and constructor initializers are outside this analysis.
- A fragment is a contiguous run of complete sibling statements in one `BlockSyntax.Statements` or `SwitchSectionSyntax.Statements` list belonging to an eligible owner. Include nested blocks of that owner, but do not cross statement-list or executable-owner boundaries. A nested local function is analyzed independently. Statements containing a nested local-function or lambda/anonymous-method body are barriers for the enclosing owner's fragments; their contents cannot be counted twice in that owner.
- A fragment must contain **at least three direct sibling statements and at least 60 original non-trivia syntax tokens**. Count tokens within those complete statements, including nested syntax and punctuation, but excluding the enclosing list's braces, trivia, missing tokens, and end-of-file tokens. These are fixed selection floors, not empirically calibrated guarantees.
- At least two occurrences must belong to distinct eligible executable declarations, identified internally by `(projectPath, sourcePath, declarationStartOffset)`. This also distinguishes local functions with otherwise identical display names. Repetition entirely within one owner is not reported. Multiple qualifying occurrences in an owner can still participate when another distinct owner has the same fragment.
- Exact copied fragments also qualify, including those surrounded by sufficiently different code that whole-body token similarity misses them. Changes to whitespace/comments do not create additional occurrences; occurrences are concrete source spans, not alternative normalized representations of one span.

### Normalization and equality

Compare the ordered syntax-node kinds and token kinds/values of each fragment. Ignore whitespace, comments, and other trivia. Preserve node/statement boundaries so unrelated token sequences cannot match by accidental concatenation.

Using the loaded semantic model, replace only identifier tokens that declare or reference an `ILocalSymbol` or `IParameterSymbol`. This includes bound foreach/catch/pattern/deconstruction locals. Assign a placeholder on that symbol's first occurrence in the fragment, using separate local and parameter categories and first-occurrence order; reuse it on every later reference to that exact symbol. Reset the mapping for each fragment. This retains distinctions between one variable used repeatedly and several different variables, including shadowed declarations. Parameter names and declaration positions outside the fragment are not compared.

Each placeholder also retains a recursive value-type key: named types use containing assembly identity and fully qualified original-definition identity plus their constructed type arguments; arrays retain rank and element type; pointers retain pointed-at type; type parameters retain their declaring symbol/assembly identity, parameter kind, and ordinal; dynamic has its own marker; function pointers retain calling convention and parameter/return types and ref kinds. Retain nullable annotation at every level. Do not normalize different value types into the same placeholder. Resolve declarations and references through symbol binding, never by identifier spelling.

Everything else remains literal syntax: member/field/property/method/type names, labels, argument names, keywords, operator kinds, modifiers, control-flow shape, invocation order, and literal token values. Compare non-normalized tokens by `RawKind` and `ValueText`, without lowercasing or other textual normalization. Do not canonicalize `var` into an explicit type, simplify expressions, remove guards, reorder statements, substitute different DTOs/APIs, or infer equivalent operations.

If a statement has an unresolved or ambiguous value/member binding, a dynamic member binding, or a required local/parameter value type that cannot be determined, that statement is a barrier: no fragment may include it. Non-value syntactic identifiers such as labels or tuple/argument names are retained literally and require no value binding. Missing syntax/semantic models or compilations fail the analysis under the existing `ANALYSIS_FAILED` contract; cancellation propagates. Do not silently return a partially completed scan.

Two fragments match only when their complete normalized forms are equal. There is no similarity percentage, weak-edge clustering, or allowance for edits within the matched fragment. Different surrounding code or inserted guards can leave smaller matching fragments, but the altered larger fragment does not match. Hashes can serve as internal lookup accelerators only; always confirm full normalized equality before reporting. No hash becomes a report identity.

### Groups, containment, and finding data

Group all eligible occurrences of one normalized form after enforcing the distinct-owner condition. Deduplicate the same source span in the same owner. Do not union groups through merely overlapping fragments.

Suppress a smaller group's redundant finding only when **every** occurrence of it is contained within an occurrence of one larger qualifying group, with strictly greater token count. The larger occurrence must have the same project, source file, and owner as the contained occurrence. If a smaller group has an additional occurrence not covered by that larger group, retain it. Partially overlapping groups without complete containment are retained. Determine this over the complete qualifying groups, independently of discovery order; there is no top-N limit or silent finding cap.

Sort occurrences by ordinal project path, source path, owner symbol ID, then start offset and span length. The first occurrence owns the finding's representative project/source, `SubjectId`, and `StartLine`. Use the owner's DocId when available; otherwise use its fully qualified symbol display suffixed with `@<declarationStartOffset>` to distinguish local-function declarations. The same convention applies to evidence labels and represented symbols for this analysis. Use `Discriminator: structural-duplicate:<startOffset>:<spanLength>`, with zero-based Roslyn UTF-16 offsets and invariant decimal integers, to distinguish several fragments of one subject through the existing identity tuple. Do not introduce a separate finding ID or hash.

Record exactly these numeric metrics: `memberCount` (occurrences), `executableCount` (distinct owners), `statementCount` (direct sibling statements per fragment), and `tokenCount` (original syntax tokens per fragment). Exact normalized equality gives equal statement/token counts across occurrences.

For every occurrence, emit one evidence entry in occurrence order: its source path, one-based first-token line, owner symbol ID as label, detail containing project path and one-based start/end line and column, and the actual trimmed first-token source line as snippet. The start is the first token's `SpanStart`; the end is the last token's `Span.End` (exclusive). Derive line/column from the loaded `SourceText` and add one to both zero-based coordinates. Keep that source line as the existing validator's loaded-snapshot evidence; multiline regions are described by their positions, not copied into reports. `RelatedSymbols` includes every occurrence's project, file, owner symbol ID, and first-token line using the existing model's deduplication rules. Evidence retains distinct occurrences even when their related symbol location deduplicates.

All occurrence files therefore participate in normal changed-file selection. Relationships to size, control-flow, existing duplication, or other findings use the existing project-and-symbol matching wherever symbol IDs agree. Do not infer relationships for a local-function fallback that differs from another analysis's symbol ID. No new relationship rules or finding-model fields are needed.

### Markdown output and limitations

Use the existing `<analysisId>.md` analysis-file mechanism. Each finding displays:

- `Structural duplicate: <memberCount> occurrences in <executableCount> executable members; <statementCount> statements / <tokenCount> tokens; identical after local/parameter normalization.`
- Every occurrence's project-relative file, owner DocId, and one-based start/end line and column. This analysis deliberately includes fragment locations because one owner can contain several matches; other analyses keep their current location rendering.
- The fixed explanation that statement/control-flow shape, operators, literals, and member names are retained, while bound local/parameter names are normalized, plus the review question above. Put the explanation/question once in the analysis header, not once per occurrence.

Use a dedicated structural-duplicate rendering branch for all groups, including groups with multiple occurrences in one owner. Render evidence in occurrence order; the existing generic cluster-symbol list alone is insufficient to distinguish regions. A fragment location is a source-navigation aid, not a new hyperlink/HTML anchor or a refactoring instruction.

This is an intentionally bounded structural signal. Equivalent implementations written with different syntax, different APIs/types/literals, reordered statements, smaller fragments, expression bodies, and excluded executable kinds can be missed. Conversely, adapters or validation routines can legitimately share the matched shape. Matching member spelling does not prove identical bound API behavior or interchangeable inputs. The agent reads the reported source and decides whether there is a maintenance problem. No measurements or precision claims are prerequisites for implementation.

### Minimal structural reference case

Use this block-bodied method in a production fixture:

```csharp
static System.Collections.Generic.List<int> Build(int[] values)
{
    var result = new System.Collections.Generic.List<int>();
    foreach (var item in values)
    {
        if (item < 0) continue;
        var transformed = (item * 2) + 1;
        if (transformed > 1000) continue;
        result.Add(transformed);
    }
    result.Sort();
    return result;
}
```

A second method with the same block and renamed `values`, `result`, `item`, and `transformed` must produce a structural candidate, including when it is embedded between different surrounding statements in another method. Verify the qualifying token floor in the fixture. Changing the retained literal `1000`, the operator, member call, value type, or variable-reuse pattern must break equality of that affected fragment. A sufficiently large different unaffected fragment can still independently qualify under the same rules; tests must distinguish that from a false match of the altered region.

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

Keep the existing layout: root `index.md`, `changed-files/index.md`, `all-findings/index.md`, and `<analysisId>.md` in each view only when that analysis has findings in that view. The new analysis uses `structural-duplication-candidates.md` in this same layout. Empty successful runs still contain exactly the three indexes. No new file category or aggregate report is introduced.

Every nonempty analysis report has this section order:

1. Existing title, purpose, effective options, and review questions, including the static-test-path limitation notice for `missing-test-evidence-candidates`.
2. `## Summary`: the total number of findings in this analysis/view, then a table with columns `Project`, `Source file`, and `Findings`.
3. `## Findings`: all individual items grouped under `### Project: <projectPath>` and `#### File: <sourcePath> (<count> findings)`.

Group by the exact pair `(Finding.ProjectPath, Finding.SourcePath)`. The source is the finding's representative file. Group unconditionally, including a single finding; there is no threshold or configuration switch. Sort projects and files ordinally, then findings within each group by `StartLine`, `SubjectId`, and `Discriminator`, with ordinal string comparison. The summary uses the same group order.

Each finding contributes exactly one to its group and analysis/view total. Cluster members, partial declarations, and forwarding-path evidence in other files do not create additional group entries or counts. Their locations remain in the individual item's existing member/path list. The same source path in different projects belongs to separate groups.

The file heading supplies the representative path, so remove its repetition from an ordinary item's label. Keep the subject DocId, existing compact signal, category, and related-analysis indication. Project, file, analysis, subject, and the existing category/represented members identify the item; do not generate another identifier. Distinct findings at the same subject remain separate items.

Keep the existing special rendering: cluster member paths/DocIds, forwarding-path members in stored evidence call order, and the shortest indirect test path. Add the dedicated structural-fragment rendering defined above. Group counts never replace the individual items or imply that an entire containing class is untested. Continue omitting complete raw metrics, long rationale, and snippets. Agents use the reported symbols and paths to inspect the source.

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

CLI commands, process responses, error messages/codes, logging, and `PublishedReport` remain unchanged. Counts include findings from the new analysis when configured. `baseline` still creates no report. The manual audit receives the same grouped Markdown through the existing writer when its profile enables the new analysis, with its current external output and baseline command. Previous published runs are not rewritten.

## Verification and acceptance

Tests verify correctness; they do not measure usefulness, noise reduction, performance, or coverage.

- Reproduce the internal executable-entry-point type defect with a failing automated regression before fixing it. Verify both API modes, synchronous/asynchronous entry points, a non-`Program` startup type, nested/partial containing types, top-level statements, and an explicit `partial Program`.
- Verify unused ordinary methods in protected types and unrelated types still become candidates. Verify a library's `Main` remains unprotected and selecting one of multiple startup types protects only the actual entry-point chain. Retain existing public API, indirect-use, uncertainty, generated-source, and test-project tests.
- Verify structural matches after consistent local/parameter renaming, including different surrounding code and cross-project occurrences. Verify variable reuse and shadowing, inferred/explicit value types, and that members, literals, operators, control-flow shape, and statement order are retained. Reject matching the affected region when retained data differs.
- Verify the fixed 60-token and three-direct-statement boundaries, distinct owners, same-owner-only rejection, exact copied fragments inside different surrounding bodies, and all supported/excluded owner kinds. Cover nested local functions, same-display-name local functions, lambda barriers, unresolved/ambiguous/dynamic binding barriers, generated/test exclusions, scan failure, and cancellation.
- Verify exact-equality groups, source-span deduplication, complete-containment suppression, partial overlap, additional uncovered occurrences, multiple fragments per owner, stable representative/discriminator/evidence ordering, and valid first-line snippets. Verify structural findings retain every occurrence location and change selection when a non-representative occurrence file changes.
- Verify production registration, behavior version/default enabled, generated configuration, the repository-root analysis entry, `enabled: false`, absent-entry compatibility, rejection of custom options, and host publication/counts for the new analysis. Existing token-duplication results/options remain unchanged, including when both analyses report an owner.
- Verify summary/group/analysis counts for one and many findings, the same source in different projects, partial declarations, cross-file/cross-project clusters, and forwarding paths. Every original finding appears once within its analysis/view; represented members are not double-counted.
- Verify subjects, signals, categories, related-analysis indications, cluster members, forwarding order, indirect-test paths, and Markdown escaping remain readable and complete under the presentation contract.
- Verify no baseline, unchanged baseline, changes to non-representative members, and added/changed/deleted C# snapshot paths. Include missing-test-evidence findings whose own files are unchanged and the non-C#-only case.
- Verify empty views, active analyses with no findings, all analyses disabled, omission of empty analysis files, and the unchanged three-index empty-run layout.
- Retain publication/host coverage for concurrent runs, older-run preservation, failed/cancelled publication, normal success responses, and no report for `baseline`. Verify editing working Markdown does not affect the complete view.
- Cover centrally hosted report output with an isolated fixture using the existing mechanisms. No additional audit of a real external repository is an acceptance prerequisite.

Run the build, FastTests, and normal IntegrationTests gates from [Build and Tests](../../docs/development/build-and-tests.md) during implementation. Do not add performance or comparative-audit gates. Update layout assertions for the grouping while retaining meaningful selection and failure assertions.

When implemented, update [Current findings](../../docs/review/findings.md) as the primary analysis/report contract, [configuration](../../docs/configuration/file-format.md) for the new enabled-only entry, and affected README, architecture, CLI registry, and build/test descriptions. Current-state documentation and the actual registry/configuration remain unchanged during concept planning. Registration and the root configuration entry are implementation requirements, not already implemented behavior.

Acceptance is the new structural-fragment signal plus corrected entry-point protection and readable grouped Markdown, with every individual finding/occurrence retained, existing selection/publication behavior preserved, and required correctness tests passing. Measured usefulness is not an acceptance condition.
