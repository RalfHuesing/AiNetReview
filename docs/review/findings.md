# Current findings

`IReviewRule.ExecuteAsync` returns a `RuleResult` containing immutable `FindingDraft` values. A draft currently carries project and source paths, a subject ID, discriminator, starting line, rationale, numeric metrics, and evidence entries. It contains no snapshot, comparison text, fingerprint, or source-file hash list.

`ReviewRunner` invokes the configured rules in ordinal rule-ID order and returns each rule's current result. It validates each draft against the loaded C# project and document paths and source text, including one-based line bounds, finite metrics, required evidence, and unique per-run identity tuples. Evidence snippets must occur on their referenced loaded source line. Findings are sorted by project path, source path, start line, subject ID, and discriminator. It retains no result between calls. Rule exceptions and invalid findings fail the whole analysis; cancellation propagates without returning a partial result.

`MarkdownReportWriter` formats the complete result as UTF-8 Markdown with LF line endings. It writes one index and one report for each configured rule, including rules with no findings. Findings, evidence, metrics, options, and rules have deterministic ordering; Markdown text is escaped and source links use encoded relative path segments. Each report set is assembled in a unique temporary directory and published by renaming that directory to its UTC timestamp and random run ID. A handled pre-publication failure removes its temporary directory, and successful runs leave older report directories unchanged.

The production registry contains only `template-noop`. `FixtureFindingRule` is confined to IntegrationTests.
