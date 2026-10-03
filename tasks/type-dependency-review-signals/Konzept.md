---
status: draft
---

# Solution-wide type dependency review signals

## Intention

Help an autonomous review agent identify concrete dependency relationships it must investigate before changing a contract or behavior. Add exactly two current-snapshot review analyses: cyclic type dependency groups and dependency hubs with both many consumers and many dependencies. Their findings must expose source evidence and useful review questions, without claiming that the measured structure proves a defect, historical drift, required changes, or an increased probability of agent failure.

Keep the first implementation small. Use explicit graph relationships, conservative absolute selection conditions, and the existing Markdown reports. Implement the selected signals, then audit their actual findings for usefulness; do not require a prolonged metrics study before implementation.

## Verified starting point

- The [current analysis contract](../../docs/development/adding-review-analyses.md) supplies a loaded Roslyn solution through `ReviewContext` and receives immutable findings with metrics, evidence, related symbols, and separate finding subjects. Production analyses are explicitly registered and declared in the root configuration.
- [SolutionReferenceIndex](../../src/AiNetReview.Core/Analysis/SolutionReferenceIndex.cs) indexes bound type and method references with project, source, production/test, generated-code, and self-reference provenance. Its simple-name scan is not a complete dependency graph: member access through inferred receivers, implicit constructors, and property/field/event operations need explicit consideration.
- [MissingTestEvidenceSemanticGraphBuilder](../../src/AiNetReview.Core/ReviewAnalyses/MissingTestEvidenceCandidates/MissingTestEvidenceSemanticGraphBuilder.cs) demonstrates semantic operation traversal and cross-project calls. It does not resolve runtime interface or virtual dispatch. It is currently owned by the test-path analysis, not a general type graph service.
- The existing forwarding analysis examines narrow transparent method chains within a project and explicitly discards cyclic paths. Size, control-flow, dead-code, and duplication analyses do not measure the two proposed structures.
- The [audit map](../../src/AiNetReview.Core/Reporting/AuditMapReportWriter.cs) groups existing findings for navigation. Cross-analysis relationships use shared symbol identities; there is no aggregate structural risk score.
- Baseline selection ordinarily uses participating file hashes. Missing-test-evidence already uses snapshot-wide C# selection because relationships can change through other files. This is not Git history or a historical semantic graph.

## Scope

### Must

- Analyze the entire loaded C# solution, including references across production projects. A project boundary must not terminate dependency collection.
- Select findings from explicit, non-generated production source types. Collect direct references from non-generated test source types separately as context; test references must not increase production counts, connect production components, or make a finding test/mixed-origin.
- Reuse the existing production/test and generated-source classifiers. State these scope boundaries in report guidance even when no finding is selected.
- Use one common definition and owning graph-building mechanism for both analyses. Keep it stateless and bounded to one loaded snapshot; no general graph framework or persistent cache is required.
- Register both analyses through existing descriptors, options, configuration, runner, validation, Markdown reporting, and audit-map mechanisms when implementation is commissioned. Both are enabled by default and listed in the root configuration.
- Report every qualifying group or type once, with stable identity, deterministic ordering, raw counts, source links, and an explicit investigation question. Do not impose a top-N finding cap.
- Verify technical correctness with automated tests, then perform one evidence-based audit of the real findings on AiNetReview, including qualifying and nonqualifying examples from fixtures.

### Not

- Git history, churn, co-change mining, semantic before/after graph persistence, or new baseline formats.
- Architecture configuration, inferred layers, namespace/project cycle analysis, or assertions that a dependency direction violates intended architecture.
- Relative repository rankings, percentiles, weighted scores, centrality measures, transitive impact estimates, dependency-depth metrics, or extra review analyses.
- Runtime dispatch resolution, points-to analysis, DI interpretation, reflection analysis, behavior-equivalence detection, or automatic identification of competing responsibilities.
- External metadata types, generated types/documents, implicit compiler-created types such as top-level `Program`, or test types as finding subjects or intermediate production graph nodes. Their omission is a declared boundary, not evidence of absence of runtime dependencies.
- Automatic refactoring, source suppression comments, build-breaking diagnostics, duplicate report exports, a separate service, or a new report navigation hierarchy.
- A roadmap or production changes during this concept-planning step. Changes to current-state documentation belong to the eventual verified implementation.

## Shared measurement contract

### Nodes and symbol identity

A node represents one explicitly declared named source type: class, record, struct, interface, enum, or delegate. Nested declarations have their own nodes; the containing type must not absorb their references. Partial declarations merge into one node. Generated declarations and symbols are excluded through the existing classifier; retain all eligible declaration locations for a partial type.

Normalize constructed generic types to their original definitions. Resolve cross-project symbols back to their declaring source node; do not depend on reference equality between compilations or simple type names. Identity includes the declaring project and stable symbol identity, so same-named types in different projects remain distinct.

### Directed edges

`A -> B` means that eligible code owned by type A has a semantically bound, direct static dependency on eligible source type B. Record the source location and one of these evidence kinds:

1. **Declaration or explicit type use:** base types, implemented interfaces, generic constraints, field/property/event types, method/delegate return and parameter types, and explicit type uses in executable code such as local declarations, casts, patterns, `typeof`, and object creation.
2. **Bound member use:** calls and method groups, constructors, property/field/event references, and user-defined operators/conversions. The target is the member's declaring type, including inherited and extension members. A method group proves a static dependency, not an executed call.

Traverse arrays, nullable/pointer types, function-pointer signatures, and generic type arguments to recover eligible named source types. An external wrapper such as `List<Order>` contributes `Order`, not a metadata node for `List`. Declaration dependencies on a property's type belong to the declaring type; a property use by another type does not invent a transitive dependency on that property's return type.

References in executable members, initializers, local functions, and lambdas belong to their containing named type. Do not create edges from lexical containment, namespace imports, attribute uses, `nameof`, comments, string literals, or unbound/dynamic targets. Inheritance edges remain visibly distinguishable from member-use edges.

An interface or virtual member use targets its statically declared member owner. Do not add guessed edges to possible implementations. Test-to-production edges use the same definition but remain contextual.

Drop self-edges. Deduplicate node pairs for graph structure and counts, regardless of reference-site count or evidence kind. Keep one deterministic source witness per pair and evidence kind; repeated uses must not inflate measurements or generate repeated findings. Witness selection and output ordering use project path, source path, source position, and stable symbol identity.

Missing compilation, syntax root, or semantic model fails analysis rather than producing an apparently complete graph. An unresolved individual binding contributes no guessed edge; retain its source-local uncertainty and label affected finding context as incomplete. The measurements describe known bound edges. Uncertainty neither proves an extra edge nor invalidates a cycle already witnessed by bound edges.

### Roslyn implementation and cost

Enumerate eligible declarations and their `INamedTypeSymbol` identities solution-wide before resolving edges. Use declaration symbols/signature types and semantic binding of explicit type syntax; traverse `IOperation` for member uses, including inferred receivers and implicit object creation. Handle attributes and `nameof` as exclusion boundaries before traversing their contents.

Adapt useful source enumeration, normalization, and semantic traversal mechanisms already present. Do not silently change the reference index or existing analyses' measurements to fit this task. A shared type graph builder in Core is sufficient; each analysis may build its graph during execution without introducing cross-analysis cache infrastructure.

After semantic collection, adjacency counting and strongly connected component detection are `O(V + E)` with `O(V + E)` graph storage, excluding stored source witnesses and deterministic output sorting. Actual semantic traversal cost and runtime must be observed, not promised. Do not enumerate every simple cycle or repeatedly search the entire solution for each type.

## Feature 1: Cyclic type dependency groups

Analysis ID: `type-dependency-cycle-candidates`.

### Problem and exact selection

A mutually dependent group has no one-way dependency order between its members. A contract or behavior change warrants checking the concrete relationships within that group; the signal does not prove that every member needs modification.

Compute maximal strongly connected components of the production-only graph. Select a component when it contains **at least three distinct types and at least three distinct eligible declaration files**, considering all partial declarations. Emit one finding per selected component, not one per cycle. These fixed floors deliberately omit smaller cycles to start conservatively; they are product heuristics, not validated risk boundaries. This analysis has only the standard `enabled` option.

### Finding and evidence

Expose type, declaration-file, project, and distinct internal-edge counts. Include every participating type with its declaration locations, every internal edge's retained source witnesses, and one deterministic simple cycle as an illustrative witness. A component may contain several overlapping cycles; the illustrative cycle need not contain all component members.

All component types are finding subjects. Pick the representative declaration deterministically; retain cross-project subjects and related symbols through existing finding contracts.

Hypothetical compact signal:

> 6 production types in 5 files form a mutually dependent group (9 directed dependencies). Example: `Planner -> Executor -> ResultMapper -> Planner`. Review the participating contracts and whether this dependency structure is intentional.

### Existing-rule gap, limits, and priority

The forwarding rule excludes cycles and only follows transparent forwarding; local size and control flow cannot reveal this component. Cyclic coupling is an exact relationship, although visitors, bidirectional object models, and callback protocols can make it acceptable. Declaration relationships do not prove runtime recursion. Partial declarations can increase the file count without increasing the number of responsibilities. Excluded code and dynamic/external bindings may hide additional relationships.

Prioritize this feature because it supplies concrete cross-file evidence about a missing one-way dependency order without needing project-specific architecture knowledge.

## Feature 2: Dependency hubs

Analysis ID: `type-dependency-hub-candidates`.

### Problem and exact selection

A type with many direct production consumers and many direct production dependencies joins two substantial sets of relationships. Before changing its contract or behavior, an agent has a concrete consumer/dependency neighborhood to investigate. This does not prove responsibility concentration or predict which consumers need modification.

For production node T, define `fanIn = |{ A : A -> T }|` and `fanOut = |{ B : T -> B }|` over distinct production nodes. Select T only when **both** `fanIn >= minFanIn` and `fanOut >= minFanOut`. Defaults are **10 and 10**. Both options are positive integers, inclusive, independently configurable through ordinary analysis options; no `testOptions` are needed. These are explicit starting heuristics to audit, not universal risk boundaries. Do not combine the values into a score or count repeated reference sites as extra neighbors.

### Finding and evidence

Expose both measured values and their effective thresholds, distinct neighbor-file/project counts for each direction, and a separate direct test-consumer count. Emit one finding per selected production type. List every direct production neighbor and its retained edge witnesses, and direct test consumers as labeled context. File counts use all eligible declaration files of the relevant neighbor set; they are descriptive, not additional selection gates.

Only T is a finding subject. Neighbors and tests are related context, so a production finding remains production-origin. Retain T's partial declaration locations as evidence.

Hypothetical compact signal:

> `PolicyResolver` has 18 production consumer types (minimum 10) and 12 production dependency types (minimum 10). Before changing its contract or behavior, inspect the listed consumers and dependencies. Four test consumer types are listed separately.

### Existing-rule gap, limits, and priority

Dead-code detects lack of known use, not breadth of use. Size and control-flow rules can miss a small dependency hub. Composition roots, facades, and central services can legitimately meet both conditions. A stable, widely used contract is not inherently problematic; requiring both directions avoids selecting every high-fan-in DTO or interface, but does not eliminate acceptable designs. Compile-time references do not prove execution or actual change propagation.

Prioritize this feature because it exposes an inspectable neighborhood that complements local findings and cyclic groups, using the same graph definition and a simple selection predicate.

## Reporting and baseline behavior

Keep the existing Markdown area/view and audit-map contracts. Put counting definitions, scope exclusions, and review questions in analysis guidance; put concrete counts, subjects, neighbor relationships, and source witnesses in findings. Preserve existing symbol-based relationships to size, duplication, and other findings. No new report format or composite hotspot layer is needed.

For both analyses, `all-findings` contains the complete current result. Without a baseline, that remains the normal audit view. With a baseline, use the existing **snapshot-wide C# selection pattern**: any added, changed, or deleted C# snapshot path selects every current finding from these two analyses; an unchanged C# snapshot selects none. A deleted reference can alter an unchanged type's counts or component, and current finding evidence cannot contain a source file that no longer exists. File-local selection alone is therefore insufficient.

Label this selection policy explicitly. It is conservative and may select unaffected findings; it does not claim that every selected finding changed. Keep other analyses' selection contracts unchanged. Project/solution/configuration changes without C# snapshot changes are not covered by source-hash selection and require an explicit full audit. Do not introduce historical graph comparison or expand the baseline format in this task.

## Verification and audit of the findings

- Verify bound member uses through inferred receivers, constructors including implicit object creation, signatures, generic arguments inside external wrappers, inherited/extension members, nested and partial types, and cross-project symbol resolution. Repeated sites must not inflate counts. Test/generated/metadata references and excluded syntax must obey the declared boundary.
- Verify SCCs against acyclic graphs, self/two-type cycles, qualifying groups, overlapping cycles in one component, partial declaration files, and exact inclusive floors. Every reported cycle edge must have a matching bound source witness.
- Verify fan-in/fan-out independently and together at their thresholds. High fan-in alone and high fan-out alone do not select a hub. Distinct test consumers remain separate. Options and generated configuration expose the stated defaults and reject invalid values.
- Verify stable finding identities/order across repeated execution, valid source links, cross-project subjects, production-origin findings with test context, complete neighbor evidence, empty-result behavior, cancellation/failure, and snapshot-wide selection including deleted C# references.
- Run the required affected test suites and repository gates for the eventual implementation. Do not add a routine benchmark suite or claim a runtime budget without measurement.
- Perform one full audit of the new analyses' findings on AiNetReview. Review every emitted finding against its sources, relevant callers/contracts, and existing findings. Record measurement correctness, whether the structure is intentional, and whether it adds a concrete useful review question; do not treat an accurate but unhelpful signal as product success.
- Use fixtures to inspect intended negative cases and acceptable designs as well. If AiNetReview yields no real findings, record that limitation rather than claiming real-world usefulness was demonstrated. Do not lower thresholds merely to manufacture findings.
- Report concrete examples and observed runtime; no unsupported claims about token savings, maintainability gains, or error probabilities. Propose changes when noise is dominated by a recurring irrelevant edge kind, poor selection, or redundant context. Remove or revise a weak signal instead of adding scores or increasingly elaborate exceptions. Changes to these product semantics require a renewed concept decision, not silent implementation tuning.

## Planning result

Exactly two analyses are selected. The solution-wide scope, current-snapshot-only operation, production selection with separate test context, graph definition, starting thresholds, reporting, and verification boundaries are specified above. This concept remains `status: draft`; review and approval belong to [workflow step 2](../../.agents/agent-workflow/02-konzept-pruefung-und-freigabe.md), which the user starts separately.

## Review decision (draft only)

The first independent read-only review identified one blocking ambiguity in the unresolved-binding paragraph: the propagation boundary of "affected finding context" is undefined. An unknown dependency originating outside a selected type/component can affect incoming counts or component membership, so source-local annotation alone must not imply complete graph coverage. No other actionable findings were reported. Verification against `SolutionLoader` confirmed that ordinary compilation errors already fail loading; the remaining decision concerns an unexpectedly unavailable binding needed for the graph, not intentionally excluded dynamic or external dependencies.

Pending user decision: either fail the analysis, and therefore the audit run under the existing runner contract, with a source-local explanation when a required static binding cannot be obtained; or continue with known edges and an explicit graph-wide incompleteness notice, without selective uncertainty propagation. The first variant is recommended for KISS because it avoids a partial-result completeness protocol. Neither variant is approved yet. Keep `status: draft` until this behavior is decided, the measurement/reporting/verification clauses are reconciled, and the release criteria of workflow step 2 are met.
