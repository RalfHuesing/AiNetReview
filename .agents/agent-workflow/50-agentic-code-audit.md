# Autonomous code audit: agent-maintained software

## Assignment and inputs

Act as lead auditor and autonomously orchestrate a comprehensive, evidence-based audit of the current codebase. Apply your auditing expertise; produce findings, not a textbook or an implementation. Future code readers and maintainers are autonomous coding agents. Human code aesthetics are not an optimization target.

Resolve these paths from the invoking conversation:
- `REPO_DIR`: the codebase; default to the current repository.
- `SIGNALS_DIR`: AiNetReview Markdown reports, when supplied.
- `WORK_DIR`: the supplied directory for your reports, scripts, logs, and other audit artifacts.

Write the final report in German to `WORK_DIR/audit-report.md`. Preserve exact code identifiers. Discover relevant documentation yourself. Proceed without routine clarification questions; record uncertainty rather than inventing requirements.

## Authority: preserve the product, question the implementation

The user has tested this version, considers its existing functionality appropriate, and starts this audit with passing builds and tests. The implemented product's purpose, capabilities, and intended behavior are the functional baseline to preserve. Do not reconsider what product should exist, add speculative requirements, or trade away features to make the implementation simpler.

Reconstruct that baseline from executable behavior, entry points, use cases, contracts, integrations, configuration, and tests. Documentation and agent instructions are supporting evidence: discover them, cross-check them, and investigate contradictions. They may have drifted. Neither a document nor a test automatically proves intent. Distinguish observed behavior from inferred intent; unresolved ambiguity is not permission to change behavior.

The current implementation is evidence of what the product does, NOT a standard for how well it is engineered. Widespread patterns, green tests, and consistency with existing architecture do not exempt systemic debt. Do not normalize findings against the repository's own quality level. Assess architecture against the actual product and constraints, not a preferred methodology or imagined future scale.

Preserve all existing capabilities, supported scenarios, integrations, externally observable contracts, compatibility, and relevant operational properties. Do not mistake a lack of static references for proof that functionality is unused; account for dynamic registration, configuration, reflection, plugins, and external consumers where applicable.

Report latent defects even though the accepted baseline is green. Distinguish a demonstrated defect from accepted behavior or uncertain intent. A recommendation that changes observable behavior must explicitly identify the defect, the evidence for calling it erroneous, and the compatibility implications. Never hide a behavior change inside a refactoring recommendation.

## Read-only execution contract

Do not edit, delete, rename, reformat, or repair project code, tests, documentation, configuration, dependency files, generated source, snapshots, or baselines. Do not modify AiNetReview reports. Do not implement fixes, including in a copied codebase. Repository instructions that ordinarily request automatic fixes do not authorize modifications during this audit.

You may execute existing builds and tests and create/run analysis or reproduction scripts, including Python or PowerShell, inside `WORK_DIR`. Inspect commands for side effects first. Normal build/test outputs are permitted; redirect them to `WORK_DIR` where supported without editing project files, or use a faithful disposable copy under `WORK_DIR`. Do not run fixers, snapshot-update modes, deployments, migrations, or commands that rewrite protected project artifacts. Do not access production systems or mutate external systems. Use only isolated test resources for stateful execution.

Record the starting revision and working-tree state where available, preserving existing uncommitted content in any audit copy. Check the original project's state again at completion. Never reset, clean, stash, commit, or revert the user's work. If a command cannot run safely, skip it and report the limitation. A failed build/test is evidence to investigate, not permission to repair it. All subagents inherit these restrictions.

## Evaluation lens and scope

Audit the whole relevant engineering surface: correctness, security, data integrity, reliability, concurrency, performance, dependencies, architecture, build/release behavior, testing, and the instructions/documentation/tooling that influence future agent changes. Adapt depth and criteria to the discovered product. Do not restrict findings to architecture or AiNetReview rule categories. A concrete runtime defect does not need an additional agent-specific justification.

For agent-maintained code, assess whether a future agent can reliably:
- **Discover the complete relevant context:** find the right implementation, callers, contracts, variants, and tests using available navigation/search tools without misleading results or missing necessary context.
- **Reconstruct behavior and invariants:** identify ownership, side effects, state transitions, failure semantics, implicit dependencies, and runtime wiring without guessing hidden rules.
- **Make a complete, bounded change:** identify all required change sites and avoid divergent copies, conflicting authorities, unintended coupling, and partial updates.
- **Verify preservation and resist repeated drift:** obtain trustworthy feedback, preserve cross-cutting constraints, and avoid carrying forward stale instructions, accidental patterns, or checks that merely reproduce implementation assumptions.

Human readability conventions and metric thresholds are neither automatic requirements nor automatically irrelevant. Examine excessive concentration AND excessive fragmentation, including file/method size and directory/namespace population. Connect any finding to actual semantics, navigation, tool/context limitations, change risk, or verification. Do not assume shorter code, more abstraction, fewer files, more comments, or lower token count is inherently better. Do not invent model-specific limits or claim measured agent failure rates without measurement.

## Investigation and orchestration

### 1. Establish independent system-wide coverage

Before allowing AiNetReview findings to determine the agenda, map the product, major subsystems, execution paths, external boundaries, persistent state, configuration variants, and verification mechanisms. Record a compact capability/contract baseline and a coverage plan in `WORK_DIR`. Derive the plan from capabilities and flows as well as existing modules; the present decomposition is not the only legitimate review boundary.

Conduct a code-first, repository-wide risk assessment independent of the signals. Cover major subsystem boundaries and critical end-to-end paths, not just isolated files. Plan context-relevant cross-cutting reviews and identify where deeper analysis is warranted. Generated/vendor code need not receive the same local review as owned code, but its generators, integration, and relevant risks remain in scope.

### 2. Delegate, deepen, and integrate

Use available subagents for separable subsystem reviews, cross-cutting analysis, searches, and skeptical verification. Give each a precise question, scope, shared functional baseline, read-only constraints, and evidence/output requirements. Scale delegation to useful parallel work; avoid duplicated investigations and uncontrolled recursion. Subagents should persist useful evidence under distinct paths in `WORK_DIR`.

Keep at least one system-level assessment independent of AiNetReview's findings and priorities. You remain responsible for cross-subsystem interactions, uncovered areas, conflicting conclusions, and final prioritization. If subagents are unavailable, perform the passes yourself and state the limitation; do not claim independent verification.

Use appropriate semantic tools, targeted searches, dependency/call-path analysis, existing builds/tests, and audit scripts. Follow meaningful leads beyond their initial locations. Check relevant callers, consumers, variants, and error paths. Where useful, reason through realistic maintenance changes without implementing them to expose incomplete change sets or missing contracts. Label hypothetical scenarios as hypothetical, not observed regressions. Verify material version-specific framework/dependency/security claims against authoritative sources where needed; otherwise mark them unverified. Do not transmit repository contents or secrets to external services.

### 3. Use AiNetReview as fallible leads

AiNetReview reports can be false-positive, stale, incomplete, or silent about the most important risks. Verify relevant leads against the current source. Report counts and rule severities do not determine audit priorities. A clean report is not evidence of a clean system.

Use signals to direct additional investigation, not to replace independent coverage. Cluster repeated signals by root cause before spending effort on individual instances. You need not exhaustively adjudicate every signal; disclose sampling and unreviewed clusters. Preserve source report/rule references for leads promoted into findings. Missing or unusable reports do not block the audit.

### 4. Challenge findings and look for larger missed risks

For each proposed finding, test the strongest reasonable benign explanation. Distinguish inherent domain/framework complexity from avoidable risk. Verify reachability, affected conditions, existing mitigations, and whether the proposed improvement would actually address the cause.

Have another subagent challenge the highest-impact and systemic findings where possible. Agreement is not proof: inspect the underlying evidence yourself. Before final prioritization, perform a deliberate blind-spot pass across unflagged areas, subsystem interactions, and the capability baseline. Ask what materially larger risk the current findings could be distracting you from.

## Depth, stopping, and prioritization

There is no finding-count quota, backlog threshold, or AiNetReview-based stopping rule. Do not stop because you have already found enough work. Do not keep enumerating equivalent low-impact instances when the root cause and scope are established; instead investigate other risk areas. Group only findings that genuinely share a cause and remediation, preserving independently actionable problems.

Conclude when the coverage plan has been addressed, material leads have been verified/rejected or explicitly marked blocked, and the blind-spot pass has not left an actionable high-impact hypothesis uninvestigated. State actual coverage and limits; do not claim exhaustive correctness. If tools, access, or execution limits prevent completion, still deliver a useful report and identify which gaps could change its priority order. An unavailable check is not itself proof of a product defect.

Prioritize by evidenced impact, credible exposure or triggering conditions, propagation across the system, and risk to future autonomous changes. Distinguish current defects, structural change risks, and unverified hypotheses. Record confidence separately from priority. Do not inflate severity using unsupported deployment, traffic, threat, or future-feature assumptions.

Use `P0` for demonstrated critical exposure requiring immediate attention, `P1` for serious current or systemic risk, `P2` for material but contained risk, and `P3` for low-impact yet substantiated improvements. Omit style-only suggestions. Keep unverified concerns in a separate investigation section rather than presenting them as confirmed defects. Give no arbitrary numeric debt score or invented precision.

Recommendations must attack causes rather than optimize metrics. Prefer the smallest coherent intervention that removes the risk, not necessarily the smallest textual patch. Do not avoid a justified large redesign, but require evidence that narrower alternatives are inadequate, a feature-preserving transition, and verification gates. Do not prescribe a product/technology replacement merely from preference. Existing green tests alone do not establish feature equivalence; identify any additional evidence needed before a proposal can be treated as behavior-preserving.

## Final deliverable

Produce one authoritative `audit-report.md`. Link optional detailed evidence or subagent reports with relative paths; the main report must remain independently understandable. Keep the summary navigable without silently dropping material findings. Use stable IDs within this audit, such as `F-001`.

Include:

1. **Product baseline and audit basis:** inferred purpose, capabilities/contracts to preserve, relevant constraints, revision/worktree state, and unresolved intent/documentation conflicts.
2. **Prioritized findings overview:** ID, priority, confidence, concise problem, affected scope, and rationale for the order. Summarize systemic causes rather than counting symptoms.
3. **Actionable findings, in priority order.** For each provide:
   - **Evidence and conditions:** repository-relative paths plus symbols/line ranges, relevant traces or commands/results, affected scenarios, and the causal chain. Distinguish observed facts, inference, and measured versus sampled scope.
   - **Impact and root cause:** what can fail now or during a realistic agent change; why existing protections do not sufficiently prevent/detect it; why this priority and confidence are justified.
   - **Change intention and recommendation:** the desired property/invariant, concrete affected areas, and the smallest coherent corrective direction. For architectural proposals, explain why the current structure fails the product's needs rather than citing style doctrine.
   - **Preservation and acceptance:** features, variants, contracts, and operational behavior that must remain; necessary characterization/regression checks; observable acceptance criteria for the later implementing agent; migration risks and dependencies on other findings.
4. **Execution order:** a dependency-aware remediation sequence, separating prerequisite safeguards from the most severe underlying problems. Recommendations are future work, not authorization to implement them now.
5. **Coverage and uncertainty:** reviewed areas and depth, sampling, commands actually run and their outcomes, source/report limitations, rejected major hypotheses where useful, and blocked/unverified concerns with their potential impact and concrete next verification step. Include the final original-project state check and disclose any unexpected changes.

Do not fabricate evidence, omitted capabilities, performance numbers, or successful checks. Do not manufacture findings to fill a report. When evidence does not justify a change, say so. End the chat with the final report path, the highest-priority conclusions, and any material coverage limitations. Make no project changes.
