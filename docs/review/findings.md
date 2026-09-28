# Current findings

`IReviewRule.ExecuteAsync` returns a `RuleResult` containing immutable `FindingDraft` values. A draft currently carries project and source paths, a subject ID, discriminator, starting line, rationale, numeric metrics, and evidence entries. It contains no snapshot, comparison text, fingerprint, or source-file hash list.

`ReviewRunner` invokes the configured rules in ordinal rule-ID order and returns each rule's current result. It validates each draft against the loaded C# project and document paths and source text, including one-based line bounds, finite metrics, required evidence, and unique per-run identity tuples. Evidence snippets must occur on their referenced loaded source line. Findings are sorted by project path, source path, start line, subject ID, and discriminator. It retains no result between calls. Rule exceptions and invalid findings fail the whole analysis; cancellation propagates without returning a partial result.

The production registry contains only `template-noop`. `FixtureFindingRule` is confined to IntegrationTests.
