---
status: ready
---

# Indirection drift candidates

## Intention

Repeated agent edits can add forwarding methods and layers while keeping the program compilable and its tests green. The next reviewer then has to follow more files to understand one operation. `indirection-drift-candidates` identifies a specific, inspectable form of this structural risk: a chain of transparent method forwarding across at least three production C# source files in one project. It asks whether each layer has a distinct responsibility. A finding is a review question, not evidence that the architecture is wrong or that the code changed recently.

This first version deliberately prefers a provable, narrow static path. Following interface implementations or dependency-injection bindings would cover more code, but could present a possible runtime route as a certain one. The narrow scope misses some real indirection; the report must state that limit rather than imply complete coverage.

## Scope

### Must

- Add one production `IReviewAnalysis` with stable ID `indirection-drift-candidates`, behavior version `1`, default enabled state `true`, and no custom options. Register it explicitly and list it in the repository-root configuration. Generated configurations must include it through the existing descriptor mechanism.
- Analyze the current loaded Roslyn solution snapshot. Use the shared source classifier to exclude test projects, generated documents, and generated symbols. Consider only methods declared in production C# source within the same analyzed project. Do not read Git, previous reports, or the baseline inside the analysis.
- Identify transparent forwarding methods exactly as defined below, build their directed forwarding graph, and emit one finding for each qualifying maximal path. Include every qualifying path; do not impose a report-count limit or a weighted score.
- Include all path members in the finding's source evidence and related symbols. This makes a change to any participating source file select the finding in the existing `changed-files` view and lets existing cross-analysis matching relate findings by symbol.
- Show the path in call order in the Markdown report, with each member's project-relative source path and symbol DocId, plus the number of forwarding edges and distinct files. The report must ask for review of each layer's responsibility and must not prescribe a refactoring.
- Add focused automated tests for selection, exclusions, path identity and ordering, changed-file selection, report rendering, configuration/registration, and repeated runs. Update current-state `docs/` pages and relevant indexes only when implementation exists.

### Not

- No historical trend claim. The present baseline contains file hashes, not earlier call graphs or dependency measurements. The word “drift” names a risk pattern, not a measured increase between runs.
- No semantic equivalence claim, redundant-layer verdict, automatic fix, build-breaking diagnostic, or finding suppression.
- No constructor-dependency count, transitive line count, percentile, complexity score, duplicate-code score, or amalgamated quality score. Those do not establish this specific forwarding path.
- No resolution of runtime dispatch through interfaces, virtual overrides, reflection, delegates, dependency injection, or external assemblies. The analysis reports only statically declared call targets in the loaded source snapshot.
- No cross-project path, property/accessor/constructor/operator/local-function/lambda path member, or markup/JavaScript call edge in version 1.
- No alteration of baseline format, CLI commands, general finding identity, or the behavior of existing analyses.

## Detection contract

### Eligible declarations and symbols

An eligible forwarding declaration is an ordinary C# method declaration (`MethodDeclarationSyntax`, `MethodKind.Ordinary`) with a body or expression body, including static and interface-implementation methods. It is not `async`, abstract, extern, generated, or in a test project. An endpoint may also be an ordinary source-declared interface or abstract method without a body; it cannot itself be a forwarding declaration. The method and every endpoint must have a nonempty Roslyn declaration DocId and a source declaration in a non-generated `.cs` document of the same production project. Normalize constructed generic method symbols to their source definitions for declaration lookup and identity; retain the bound constructed symbol for signature and conversion checks. If a target has several source syntax references (for example, a partial method), use the implementation declaration when present; otherwise use the ordinally first project-relative path and source position. A linked source file compiled by two projects is evaluated separately for each project.

A method is a **transparent forwarder** only when all of the following hold:

1. Its entire executable body is one direct method invocation: an expression body `=> Target(...);`, a block containing exactly `return Target(...);` for a non-`void` method, or a block containing exactly `Target(...);` for a `void` method. Parentheses around the invocation are allowed. Comments and whitespace do not affect the test. No other statement, assignment, `await`, conditional access, cast, null-forgiving operator, conditional expression, or exception handling is allowed.
2. Roslyn binds the invocation to one ordinary source method in the same project. An interface or abstract declaration can be a target and terminate a path. Delegate invocations, extension methods (including reduced extension calls), local functions, and unresolved or ambiguous targets do not create an edge. A virtual call is interpreted as a reference to its statically bound declaration only; the report makes no runtime-dispatch claim.
3. The forwarder's return type is identical to the bound target method's constructed return type under `SymbolEqualityComparer.IncludeNullability`; both are `void` for the expression-statement form. The invocation has no return-value conversion other than identity.
4. The invocation explicitly passes every wrapper parameter exactly once, in declaration order, to the target parameter at the same ordinal. Wrapper and target parameter counts are equal. Each argument is a direct reference to its corresponding wrapper parameter with an identity conversion. There are no omitted optional arguments, `params` expansion, named-argument reordering, `ref`/`out`/`in` parameters, or argument transformations. A zero-parameter call satisfies this condition.
5. The invocation receiver is a direct `this` reference, a field reference on `this`, a direct wrapper-parameter reference, or a static type receiver. No local variable, property getter, factory call, cast, indexer, or conditional receiver qualifies. The target's containing type must differ from the forwarder's containing type.

Use Roslyn syntax to enforce the exact body shape and semantic operations/symbols to enforce target, receiver, arguments, conversions, return type, and source ownership. `SolutionReferenceIndex` indexes references but does not provide this ordered forwarding graph; this analysis builds its own per-run method map and edges. A declaration with missing semantic information is not classified as a forwarder. Failure to obtain a required project compilation or document syntax/semantic model fails the analysis according to the existing analysis contract.

### Paths and finding selection

For each eligible forwarder, add exactly one directed edge from its declaration to the statically bound target declaration. Each node has at most one outgoing edge. An endpoint has no outgoing edge. Build and traverse the graph independently per project.

A **root** is a forwarder with no incoming edge from another eligible forwarder in that project. Starting at each root, follow edges until the first non-forwarder endpoint. If a node repeats, discard that traversal as a cycle; do not claim a finite forwarding path. The resulting path is maximal in this graph. It qualifies only if it has at least two forwarding edges and contains at least three distinct containing types and at least three distinct project-relative `.cs` source paths. These are fixed structural conditions, not configurable quality thresholds. One finding is emitted per qualifying root. Paths that share a suffix retain separate root findings; subpaths beginning at a non-root are not duplicated. A project with no qualifying root returns no findings.

Example: `Api.Handle -> Service.Handle -> Repository.Handle` qualifies when the three declarations are in distinct source files and the first two methods satisfy the transparent-forwarder definition. `Api.Handle -> Service.Handle` does not. A wrapper that validates input, maps arguments, catches an exception, awaits a call, or accesses the target through a property does not form a transparent edge. A call through an interface can terminate a path at the interface declaration; the analysis does not assert which implementation runs.

### Finding and report contract

- The representative source is the root method. Set `subjectId` to its declaration DocId and `discriminator` to `transparent-forwarding-path`. This produces one stable identity tuple per root method within a project. Use the existing project-root-relative `/` paths and one-based declaration line.
- Record numeric metrics `forwardingEdgeCount`, `distinctTypeCount`, and `distinctFileCount`. They explain the selection; there is no combined score.
- Add one `FindingEvidence` item per path member in call order. The forwarder evidence uses its invocation's starting source line; the endpoint evidence uses its declaration's starting line. Each item names the member DocId, identifies whether it forwards or ends the statically declared path, and includes a nonempty snippet from that exact loaded source line. Add every member as a `FindingSymbol` with its owning project, source path, DocId, and declaration line.
- The current generic multi-symbol renderer sorts related symbols by path and labels them a “Cluster”, which loses call order. Add a renderer branch for this analysis: print one “Forwarding path” item per finding, then enumerate the evidence members in their stored call order with source path and DocId. Use each evidence item's `Label` for the DocId and `SourcePath` for its file; do not infer order from `RelatedSymbols` or parse the rationale. Print the three metrics in the signal as `N forwarding edges across M types and K files`. Keep the existing report's omission of raw snippets and line numbers and do not change other analyses' report formats.
- Set descriptor purpose, measurement, and review questions to describe the exact static conditions and limits. The wording must distinguish a statically declared call chain from a proven runtime path, and a current candidate from historical growth. A suitable review question is: “What responsibility does each forwarding layer add, and is this path intentional for the architecture?”
- The existing `ReviewFindingBuilder` and report publication determine `changed-files` inclusion from finding and evidence source paths. Evidence for every member is therefore mandatory. Without a baseline, all current paths appear in both views. An unchanged path can appear in `changed-files` when a participating file changed for another reason; the report must not describe it as a newly introduced path.

## Verification

- Fast tests cover all three accepted body forms; direct receivers; generic source-definition lookup; same-project target ownership; and the exact parameter, return, body, receiver, generated/test, and unresolved-target exclusions. Include interface and virtual targets without asserting runtime implementation.
- Graph tests cover the two-edge/three-type/three-file boundary, longer paths, two roots sharing a suffix, same-file paths, cycles, deterministic output order, linked source documents, and no artificial finding cap.
- Runner and reporter tests confirm that every participating file can select the finding via the baseline, that symbol relationships use exact identity rather than file co-location, and that report path order remains call order even when alphabetical path order differs. Re-running after source edits must recompute findings without retained state.
- Host/configuration tests confirm explicit production registration, generated default configuration, repository-root configuration, a nonempty report, and omission of an empty analysis file. Relevant failure and cancellation cases must publish no partial report.
- Run the affected FastTests and IntegrationTests plus the repository's required build gate for code changes. Compare at least one real audit result against the cited source paths and classify findings as useful review prompts, intentional layers, or measurement errors; this audit informs wording and scope, not a silent change to the selection contract.
