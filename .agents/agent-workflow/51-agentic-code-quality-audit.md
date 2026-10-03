# Autonomous code-quality audit: agent-maintained software

## Mission and inputs

Act as lead auditor and autonomously orchestrate an evidence-based audit of the code's engineering quality. Future readers and maintainers are autonomous coding agents; human aesthetics are not the optimization target.

This is a structural code-quality audit, NOT a functional, business-domain, or product audit. Ask:

> With the product and its capabilities unchanged, how reliably can a future agent discover, understand, modify, and verify this implementation, and which structural changes would materially improve that reliability?

Apply your existing expertise in code review, design, refactoring, abstraction, and maintainability. Adapt it to agent maintenance; do not recite a methodology. Structural weaknesses are valid findings even when the software works, all tests pass, and no previous agent failure has been observed.

Resolve these paths from the invoking conversation:
- `REPO_DIR`: the codebase; default to the current repository.
- `SIGNALS_DIR`: optional AiNetReview Markdown reports.
- `WORK_DIR`: the supplied directory for reports, scripts, logs, and evidence.

Write the final report in German to `WORK_DIR/code-quality-audit-report.md`; preserve exact code identifiers. Discover relevant documentation yourself. Proceed without routine clarification questions; record uncertainty rather than inventing facts. This prompt is self-contained. Prior audits are not prerequisites; treat any supplied findings as fallible leads. Do not overwrite their reports.

## Fixed product, open implementation

The user has tested this version, considers its functionality appropriate, and starts with passing builds and tests. The implemented product's purpose, intended behavior, and capabilities are the baseline to preserve. Do not redefine the product or simplify it by removing features.

Infer the technical context and preservation constraints from code, contracts, entry points, runtime wiring, integrations, configuration, and tests. Discover documentation and agent instructions, but cross-check them: they may have drifted. Understand behavior only as needed to evaluate its representation and safe modification; do not perform an exhaustive feature inventory or a second functional audit. Uncertain intent is not permission to change behavior.

Do not judge whether business rules, calculations, workflows, or requirements are correct, useful, or complete. You MAY assess how they are represented, owned, coupled, duplicated, and made discoverable. Investigate scattered authorities for an existing rule, not whether that rule is appropriate.

The current architecture and prevalent patterns are NOT quality standards. Consistency, documentation, and green tests do not exempt systemic structural debt. Conversely, unfamiliar or human-unfriendly code is not automatically poor code for agents. Do not normalize your assessment against the repository's own quality level.

Preserve supported scenarios, variants, integrations, external contracts, compatibility, error behavior, configuration, persistence semantics, and relevant operational properties. Missing static references do not establish that code is unused: consider reflection, registration, plugins, serialization, and external consumers.

Keep recommendations within the existing product, language, and platform context. Substantial internal redesigns are allowed when justified; replacing the product or technology, inventing future requirements, and reducing functionality are not substitutes for better engineering.

## Scope

Cover owned production code, technical module boundaries, test code, and relevant generators. Assess local structure AND system-wide implementation architecture, not just isolated files or linter categories. Treat generated/vendor code proportionately; prefer addressing generators or integration boundaries over manually changing generated output.

Include configuration/build code, comments, documentation, and agent instructions only where they materially affect understanding, complete changes, or verification of the implementation. Do not expand into a general process, documentation, or tooling audit.

Do not conduct a separate correctness, security, compliance, or performance audit. State ownership, concurrency, error propagation, and resource lifetimes remain relevant as structural concerns. Record serious defects encountered incidentally in a separate handoff section without making defect hunting the agenda.

## Read-only execution contract

Do not edit, delete, rename, reformat, or repair project code, tests, documentation, configuration, dependencies, generated source, snapshots, or baselines. Do not modify AiNetReview reports or implement fixes, including in a copied codebase. Repository instructions requesting automatic fixes do not authorize modifications during this audit.

You may run existing builds/tests and create/run analysis scripts, including Python or PowerShell, under `WORK_DIR`. Inspect side effects first. Normal build/test outputs are permitted; redirect them to `WORK_DIR` without editing project files where supported, or use a faithful disposable copy there. Preserve uncommitted content in copies. Do not run fixers, snapshot updates, deployments, migrations, or commands rewriting protected artifacts. No production access or mutation of external systems; use isolated resources for stateful tests.

Record the starting revision and working-tree state, then check the original project's state again at completion. Never reset, clean, stash, commit, or revert user work. Skip unsafe commands and disclose limitations. Failed builds/tests do not authorize repairs. All subagents inherit these restrictions.

## Agent-oriented quality criteria

Explicitly assess these dimensions; they are not quotas for findings:

- **Discovery and semantic clarity:** Can agents find authoritative implementations, callers, variants, contracts, and tests with available search/navigation tools? Do naming, API shapes, placement, comments, and runtime wiring expose their actual role or invite a misleading interpretation?
- **Reasoning locality:** Can agents reconstruct behavior and constraints without assembling excessive, unrelated, or implicit context? Examine control/data flow, type information, indirection, ownership, lifetimes, side effects, and order-dependent protocols. Count necessary reasoning dependencies, not merely files opened.
- **Cohesion, coupling, and change completeness:** Are responsibilities and invariant ownership coherent? Can agents identify the full change set and bound its effects? Examine dependency direction, cycles, dispersed edits, duplicated authorities, variant divergence, and unnecessary shared state. Distinguish independently similar code from duplication that must stay synchronized.
- **Abstraction and complexity fit:** Does each significant layer, interface, generic mechanism, or extension point provide a concrete benefit for the present product? Examine excessive machinery AND missing boundaries. Evaluate complete paths rather than just the simplicity of individual methods.
- **Verification and drift resistance:** Can later agents verify structural changes without tests that merely mirror implementation details, excessive mocking, or hidden assumptions? Are technical contracts discoverable and, where appropriate, mechanically checkable? Assess test structure and coupling, not business-rule correctness. Do not prescribe coverage quotas or checks without a concrete preservation need.

Inspect concentration AND fragmentation: method/type/file size, directory/namespace population, partial types, forwarding methods, scattered helpers, and delegation depth. None is automatically acceptable or defective. Connect findings to navigation, semantic boundaries, context assembly, coordinated edits, or verification. Splitting files does not resolve overloaded responsibilities; merging files does not necessarily reduce relevant context.

Use applicable metrics and semantic analysis as evidence, not verdicts. State methods and sampling. Do not invent model limits, token savings, agent failure rates, or universal thresholds. Short code, familiar patterns, low token count, and human readability are not independent success criteria; obscure code is not intrinsically agent-friendly either.

A finding requires concrete source evidence and a defensible explanation of additional search, inference, coordination, or verification for a realistic maintenance task. It does NOT require an observed defect or measured agent failure. Label hypothetical scenarios as such; do not invent future product requirements to justify them.

## Overengineering and pattern alternatives

Explicitly assess whether simplification, a different pattern, an added boundary, or removing an abstraction would improve agent maintenance without losing functionality. Use SOLID and other principles diagnostically, not as compliance targets. Neither more abstraction nor less abstraction is inherently better.

For interfaces and similar boundaries, examine actual consumers, substitution, dependency isolation, lifecycle semantics, framework requirements, testing needs, and external contracts. One implementation does not prove an interface unnecessary; multiple implementations do not prove it useful. Determine whether the boundary isolates meaningful behavior or merely adds navigation.

For substantial recommendations, compare the current design with the simplest viable alternative and, where useful, another pattern. Include leaving the design unchanged. Explain what complexity disappears, moves, or is introduced and why the net result improves agent work. Scrutinize your alternative as rigorously as the existing design.

Do not justify abstraction with speculative extensibility, or remove real extensibility because only one consumer is visible. Prefer the smallest coherent intervention, not necessarily the smallest patch. Justified large redesigns require evidence that narrower alternatives are inadequate, staged migration, and preservation checks. An imperfect design alone does not justify a rewrite.

## Investigation and orchestration

### 1. Establish independent structural coverage

Before AiNetReview determines the agenda, map implementation boundaries, dependencies, runtime composition, ownership, and test structure. Record a compact preservation baseline and coverage plan under `WORK_DIR`.

Survey representative code and risky cross-module paths independently. Existing folders/projects need not be the right review boundaries. Look for avoidable complexity that is concentrated, dispersed, or normalized repository-wide.

### 2. Delegate, deepen, and integrate

Use available subagents for separable areas, cross-cutting analysis, searches, and skeptical review. Give each a precise question, scope, preservation baseline, read-only rules, and evidence/output requirements. Persist useful results under distinct paths in `WORK_DIR`. Avoid uncontrolled recursion and duplicated investigations.

Keep at least one system-wide structural assessment independent of AiNetReview. You remain responsible for cross-area relationships, coverage, conflicting proposals, and conclusions. If subagents are unavailable, perform the passes yourself and disclose that independent review was unavailable.

Use semantic navigation, reference/call/dependency analysis, targeted searches, existing builds/tests, and scripts as useful. Trace candidates through actual consumers, runtime wiring, variants, and tests. Reason through maintenance changes without implementing them. Judge discovery using available agent tools, not human scrolling difficulty.

Use relevant history when helpful; missing documented rationale does not prove overengineering. Verify material framework/version claims against authoritative sources when needed or mark them unverified. Do not transmit repository contents or secrets to external services.

### 3. Treat AiNetReview as fallible leads

Signals may be false-positive, stale, incomplete, or silent about the largest problems. Verify relevant leads against current code. Rule severities, counts, and report order do not determine priorities.

Cluster related signals by cause. You need not adjudicate every instance; disclose sampling and unreviewed clusters. Retain report/rule references for promoted findings. Missing or clean reports do not block or replace independent analysis.

### 4. Challenge findings and blind spots

Test the strongest reasonable explanation for keeping each disputed structure: required variability, compatibility, deliberate isolation, framework constraints, generated regularity, or independent responsibilities. Assess existing mitigations. Reject taste-only, slogan-based, and threshold-only findings.

Have another subagent challenge the highest-priority findings and substantial redesign proposals where possible. Agreement is not proof; inspect the evidence. Check whether alternatives introduce hidden dependencies, tighter coupling, larger coordinated edits, weaker verification, or behavior loss.

Before prioritizing, inspect unflagged areas and cross-module structures for larger missed causes. Do not let numerous local findings distract from systemic debt.

## Completion and priority

No finding-count quota, backlog threshold, or AiNetReview-based stopping rule applies. Do not stop because enough work has been found. Once equivalent symptoms establish a cause and its scope, investigate other areas rather than enumerating endlessly. Merge only findings sharing a cause and coherent remedy.

Conclude when planned coverage is addressed, material candidates are validated/rejected or marked blocked, and the blind-spot pass leaves no actionable high-impact lead uninvestigated. Disclose actual depth and gaps, not exhaustive assurance. If tools/access/execution limits intervene, deliver the report and identify gaps that could change its ordering.

Prioritize within this structural audit:
- **P1:** high-impact structural obstacles to reliable agent changes, including systemic or strongly propagating problems. A current functional defect is NOT required.
- **P2:** material but more bounded problems in comprehension, change completeness, or verification.
- **P3:** smaller, substantiated improvements; omit taste-only suggestions.

Justify priority through scope, realistic maintenance operations, hidden assumptions, coordination burden, propagation, and safeguards. Record confidence separately. Keep unverified suspicions in an investigation section. Do not invent future scale, findings, or numeric quality/debt scores.

Separate importance from execution order: characterization or boundary tests may precede a high-priority restructuring. Passing existing tests alone does not prove equivalence. Make restructuring conditional on missing preservation evidence where necessary.

## Final deliverable

Produce one authoritative `WORK_DIR/code-quality-audit-report.md`. Link optional evidence/subagent reports relatively; the main report must stand alone. Use stable IDs such as `CQ-001`. Provide:

1. **Technical context and audit basis:** compact implementation map, preservation constraints, revision/worktree state, and assumptions. No product-value or business-correctness assessment.
2. **Structural quality assessment:** address each quality dimension, including abstraction/overengineering, concentration/fragmentation, and local versus systemic causes. Identify where change is not justified; no scores or mandatory findings per category.
3. **Prioritized overview:** ID, priority, confidence, problem, affected scope, and intended improvement.
4. **Actionable findings in priority order**, each containing:
   - **Evidence:** repository-relative paths, symbols/line ranges, relevant relationships/results, and measured versus sampled scope. Separate observation from inference.
   - **Cause and agent impact:** concrete difficulty and a realistic maintenance scenario; what must be discovered, inferred, coordinated, or verified, and why safeguards are insufficient. A smell label is not enough.
   - **Change intention and recommendation:** desired structural property, affected areas, and concrete corrective direction. For pattern changes, compare alternatives, including keeping or simplifying the current design, with new costs and tradeoffs.
   - **Preservation and acceptance:** behavior/contracts to retain, migration risks, prerequisite checks, dependencies, and verifiable acceptance criteria for the later implementing agent. Success is not merely lower metrics or adoption of a pattern.
5. **Execution order:** a coherent, dependency-aware sequence without contradictory redesigns. Separate prerequisites from structural priorities. Recommendations are future work, not authorization to implement.
6. **Coverage and uncertainty:** reviewed areas/depth, sampling, AiNetReview use/limitations, actual commands/outcomes, material rejected hypotheses, and blocked concerns with verification steps. Include the final original-project state check and any unexpected changes.
7. **Incidental observations, only when necessary:** serious defects encountered accidentally, with evidence and a handoff recommendation, separate from the structural ranking. Do not claim a functional/security audit.

Do not fabricate evidence, benefits, omitted capabilities, or successful checks. If no material structural weaknesses are established, document that and the coverage rather than forcing a refactoring agenda. End the chat with the report path, highest-priority structural conclusions, and material limitations. Make no project changes.
