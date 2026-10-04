# CQ-001–003 refactoring results

Base revision: `4fe6063977bb6361488603e687ad373098b180ef`.
Inputs: `tasks/audit-code/code-quality-audit-report.md` and `audit-reporting/20261004T110754Z-db195420`.

## Implemented scope

- CQ-001: Every built-in analysis owns its finding presenter. The generic Markdown writer contains no built-in analysis IDs. Shared Markdown, file writing, finding identity, and routing helpers remove the map writers' reverse dependencies on the publication writer. Structural ranges and duplicate occurrence identities use typed evidence; the writer no longer parses coordinates or identities from prose. Analysis notes, scopes, clusters, roles, locations, escaping, and atomic publication retain their contracts.
- CQ-002: Context-owned artifact slots share one successful dependency graph across both type analyses and maps, and one source/ownership index across finding validations. Failed and canceled construction is not cached; concurrent requests serialize, canceled waiters do not cancel another caller's build, and separate contexts remain isolated. Graph collections are read-only after construction. Typed evidence is validated against project ownership and loaded source text.
- CQ-003: TestKit provides a disposable in-memory Roslyn workspace builder without a Core dependency. The FastTests adapter supplies the existing filtered references and review contexts. Compatible fixture families use synthetic paths while preserving aliases, linked sources, framework metadata, assembly identities, and compilation/parse options. Actual filesystem, generator, failure, and MSBuild scenarios retain specialized fixtures. Integration restore and repeated service composition use shared helpers.

## Verification

- Final Release build: zero warnings and errors, via `scripts/build.ps1 -c Release`; log `temp/build.log`.
- Final FastTests: 384 passed, zero failed/skipped, via `scripts/test-fast.ps1 -c Release --no-build`; log `temp/test-fast.log`.
- Final standard IntegrationTests: 111 passed, zero failed/skipped, via `scripts/test-integration.ps1 -c Release --no-build`; log `temp/test-integration.log`.
- Frozen normalized hashes for 18 complete Markdown files cover all ten built-in presentations plus a generic analysis, root/area indexes, and map indexes. Expected hashes were captured before presenter extraction and remain unchanged.
- New artifact tests cover reuse, isolation, linked-document ownership, invalid typed evidence, single-flight construction, and cancellation/failure behavior. A workspace test verifies cross-project compilation without filesystem creation.
- Independent final audit: `gpt-6.1-sol`, reasoning `medium`. Its P2 finding about unescaped custom-presenter labels/headings was reproduced by a failing test, fixed through a subagent, and rechecked. The custom-presenter regression covers all four block kinds, inline metacharacters/newlines, paths, roles, symbols, ranges, and intentionally raw Markdown notes. No material audit findings remain within this change.
- Complete CLI self-audit: `20261004T151222Z-9a4f2244`, successful publication of 258 current findings across all ten enabled analyses, with no project areas excluded because of binding uncertainty. The enabled cycle analysis produces no findings; the original reporting cycle is absent. Report: `audit-reporting/20261004T151222Z-9a4f2244/index.md`; response log `temp/refactoring-self-audit.log`.
- `git diff --check` passed. Release-only performance tests were not run; no measured speedup is claimed.

## Boundaries

The original audit reports and both pre-existing audit notes remain untouched. CQ-004, CQ-005, INC-001, and unrelated current audit signals were outside this change. Finding thresholds and production analysis registrations were not changed. Semantic navigation was unavailable because AiNetCodeNavigator rejected the existing Options source-generator provenance; focused source/caller inspection and actual product execution provided verification instead.
