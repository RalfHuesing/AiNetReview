# Current findings

`IReviewRule.ExecuteAsync` returns a `RuleResult` containing immutable `FindingDraft` values. A draft currently carries project and source paths, a subject ID, discriminator, starting line, rationale, numeric metrics, and evidence entries. It contains no snapshot, comparison text, fingerprint, or source-file hash list.

`ReviewRunner` invokes the configured rules in ordinal rule-ID order and returns each rule's current result. It retains no result between calls. It currently does not validate or normalize finding drafts; those contracts are specified in [Epic 2](../../tasks/initial-infrastructure/epics/02-Regel-und-Findings.md).

The production registry contains only `template-noop`. `FixtureFindingRule` is confined to IntegrationTests.
