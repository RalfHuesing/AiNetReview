# M2-T3 — Registration, findings and end-to-end acceptance

## Intention

Expose the completed rule through normal configuration and publish complete, useful Markdown review findings.

## Scope

- Register `dead-code-candidates` explicitly in production. Validate and resolve `apiSurface` and `entryPointAttributes` through the existing descriptor/configuration path, including defaults, accepted values and malformed input.
- Produce current, reproducibly sorted `FindingDraft` values with a declaration location, source excerpt, defensible rationale and concrete counter-check questions. Keep type grouping and one finding per candidate identity. Follow existing `ReviewRunner`, finding validation and `MarkdownReportWriter` contracts.
- Verify complete empty runs publish an empty rule report. Cancellation, incomplete coverage, rule failure or invalid findings must fail the run without publication. Add focused host/runner/report integration tests rather than copying AiNetLinter's full suite.
- Update the affected `docs/` pages and indexes to describe only the behavior now implemented.

## Non-goals

Automatic removal/refactoring, build-breaking diagnostics, MCP endpoints, or changes to prior report directories.

## Contracts and invariants

- The rule asks for human review; finding prose must not assert certain dead code or direct deletion.
- Follow the report writer's deterministic ordering and atomic publication behavior documented in `docs/review/findings.md`. See [Konzept.md](../Konzept.md) for the complete product boundary.

## Acceptance

- [ ] Production registration, option validation/defaults, deterministic Markdown evidence and review questions are covered by focused tests.
- [ ] End-to-end tests show a representative candidate, a protected declaration, a complete empty report and no report after analysis failure or cancellation.
- [ ] Affected documentation matches the verified implementation; build and affected test suites pass.

## Checklist

- [ ] Inspect current configuration, registration, finding validation, report and host tests.
- [ ] Implement this scope and update affected current-state documentation in the same slice.
- [ ] Run focused tests and required project gates; review the diff and `git diff --check`.
- [ ] Close this leaf and its parent roadmap checkbox only after acceptance is verified; commit explicit task paths.
