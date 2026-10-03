# Audit baseline and coverage plan

Established before reading AiNetReview signals. Revision: 7c4ceedb3710c445facb9746e767b9a940278d72. Initial git porcelain output: empty. Audit is read-only except artifacts in this directory and ordinary build/test outputs. No commits, fixes, releases, or external mutations.

## Capability baseline

- CLI `review [project-path]`, help and invalid-input handling; one JSON completion only after publication; failures 2/3/4 and cancellation 130; executable-relative file logging.
- Existing configuration remains intact; absent configuration generated using solution discovery. Typed validated options, enabled state and production/test variants; project and output path boundaries.
- Git-independent MSBuild/Roslyn solution loading; materialized source snapshot, compilation validation, generated-source handling and conditional safe markup snapshots.
- Ten explicitly registered analyses: control flow, dead code, duplicate code, structural duplication, forwarding, missing test evidence, identifiers, size, dependency cycles, dependency hubs. Full configured loaded-solution results; signals are candidates, not build errors or fixes.
- Shared semantic references, test/generated classification, dependency graphs, immutable current findings, stable identity and occurrence/project roles.
- Atomic publication of complete Markdown reports and source/audit maps; previous runs preserved, empty runs and disabled states disclosed.
- Five net10.0 projects; Core independent of host; FastTests, IntegrationTests and TestKit. Build analyzers, separate audit/performance gates and Windows release archive.

Observed entry points and DI registrations were inspected; docs provide supporting contracts. Semantic index reports five projects, 147 Roslyn documents including 20 generated, all configured net10.0 contexts loaded. Physical inventory and dynamic wiring also remain necessary.

## Independent review boundaries

1. Host/configuration/loading: invalid input, ownership, boundaries, snapshots, cancellation and resources; public CLI behavior through publication.
2. Reporting/findings/maps: complete routing, stable identity, integrity, escaping, failure and atomicity, related findings and role semantics.
3. Analyses/semantic infrastructure: binding and uncertainty, generated/test variants, full scope, algorithmic correctness and cost; static versus dynamic uses.
4. System-level verification: build/test/release scripts and instructions, dependency configuration, root registry completeness, end-to-end invariants and cross-subsystem interactions. This pass stays independent of signal priorities.
5. Signal pass after independent plan: cluster leads, sample with explicit scope, cross-check source freshness, avoid metric-only claims.
6. Challenge material findings with a separate reviewer; inspect original evidence; deliberate blind-spot pass and final source/report integrity comparison.

## Execution safeguards

Inspect build/test side effects before running. Integration tests include repository review output; use a faithful disposable copy under the audit directory when required. Preserve source bytes and configuration; do not run release, fixers or snapshot updates. Analysis/reproduction programs belong here. Record actual commands/results and limitations; passing tests do not prove correctness.
