# Roadmap: Shared code measurements and test roots

The contract is [Konzept.md](Konzept.md). Complete the points in order. Each implementation point includes focused contract tests, affected project gates, a check of relevant current-state documentation, its checkbox update, and an atomic commit. Do not change observable production behavior without an automated test. Do not start the follow-on review analyses in this roadmap.

- [x] **1. Extract control-flow measurement and preserve the existing analysis**
  - Intention: Give later analyses the exact current decision measurement without changing `method-control-flow-outliers`.
  - Scope: Move the existing `DecisionVisitor` behavior into the stateless `ControlFlowMetrics.Measure` contract in the concept. Return the four defined measurements, validate the body argument, and switch the existing analysis to the new entry point while retaining its current candidate enumeration, descriptor, thresholds, evidence, ordering, and behavior version.
  - Not: New counted syntax, a different decision model, or additional candidate kinds in the existing analysis.
  - Acceptance: Focused FastTests cover every decision form, depth and tie rule, empty bodies, invalid input, and nested-function exclusions from the concept. Existing analysis tests and targeted regression cases verify unchanged candidate identities, metrics, and evidence. Run affected FastTests, IntegrationTests, the build gate, the documentation check, and `git diff --check` before committing.

- [x] **2. Add token-based code-line measurements**
  - Intention: Provide one deterministic line-counting rule for the future size analysis.
  - Scope: Implement the three `CodeLineMetrics` operations from the concept: generic token-start lines, executable declarations with the no-body rule, and own type-part lines excluding nested types and delegates. Keep partial-type aggregation and file-size measurement with the future consumer.
  - Not: Size thresholds, candidate selection, `BranchTrivialityDetector`, file-byte measurements, or changes to review findings.
  - Acceptance: FastTests cover token/trivia boundaries, signatures and braces, multiline literals, invalid inputs, expression bodies, bodyless and partial declarations, local functions/lambdas, and nested types on shared and separate lines as specified in the concept. Run affected FastTests, the build gate, the documentation check, and `git diff --check` before committing.

- [x] **3. Add semantic active-test-root classification**
  - Intention: Identify possible roots for the later test-path analysis without relying on source-text names or test-project membership alone.
  - Scope: Implement `TestFrameworkClassifier.IsActiveTestRoot` with the existing project classifier, metadata-resolved xUnit/NUnit/MSTest attributes, derived attributes and xUnit v3 interface implementations, and the statically decidable whole-method/fixture exclusions in the concept. Keep generated-source filtering with the future caller.
  - Not: Call-graph traversal, test-data execution, dynamic skip evaluation, new Core framework dependencies, or registration of `missing-test-evidence-candidates`.
  - Acceptance: Metadata-referenced FastTests cover every recognized framework root, derived and interface attributes, MSTest class requirements, parameterized methods as one root, global versus per-case skips, dynamic skip cases, lookalike source attributes, ordinary helpers, and production projects. Run affected FastTests, the build gate, the documentation check, and `git diff --check` before committing.

- [ ] **4. Final audit**
  - Intention: Confirm that the completed Core foundation matches the ready concept and has not changed existing review behavior.
  - Scope: Review the three APIs, tests, outlier-analysis regression, dependency boundary, relevant `docs/` pages, and commits against every contract and scope limit in [Konzept.md](Konzept.md). Run the complete normal FastTests and IntegrationTests scripts, excluding the separately invoked manual Audit and Performance categories, plus the build gate and `git diff --check`; record concrete gaps for correction if needed.
  - Not: Implementing either follow-on review analysis, adding speculative shared abstractions, or changing selection thresholds.
  - Acceptance: Every concept requirement has implementation and verification evidence; existing outlier results and descriptor remain unchanged; no relevant test was weakened; the full solution gates pass; documentation describes only implemented behavior; and the roadmap checkboxes match the completed commits.
