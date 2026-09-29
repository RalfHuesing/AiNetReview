# Roadmap: Missing test evidence candidates

The contract is [Konzept.md](Konzept.md). [Shared code measurements and test roots](../core-code-metriken/Konzept.md) must be completed and verified before point 1 starts. At kickoff, inspect the delivered `ControlFlowMetrics.Measure` and `TestFrameworkClassifier.IsActiveTestRoot` APIs against that prerequisite; reconcile a contract mismatch before implementing this analysis. Complete the points in order. Each implementation point includes its own focused tests, affected project gates, documentation check, checkbox update, and atomic commit. Do not change production behavior without an automated contract test.

- [ ] **1. Candidate enumeration using shared control-flow metrics**
  - Intention: Produce a deterministic set of structurally nontrivial production functions from the completed Core measurement without duplicating its decision rules.
  - Scope: Select the function kinds and executable bodies in the concept, apply project/document/symbol exclusions, call `ControlFlowMetrics.Measure` on each body, and apply both pairs of complexity gates. Keep local functions and lambdas out of candidate measurements as defined by the shared API.
  - Not: Test attribution, registration, or report publication.
  - Acceptance: Focused tests of the shared API integration exercise every candidate kind, both threshold pairs, their OR/AND combination and reversed configured thresholds, expression/partial bodies, generated/test exclusions, and use of the shared metric values. Do not change `method-control-flow-outliers` or duplicate the prerequisite's metric tests. Run affected FastTests and the build gate before committing.

- [ ] **2. Semantic test-origin paths and uncertainty**
  - Intention: Distinguish direct, indirect, and absent static paths from recognized test roots without claiming runtime coverage.
  - Scope: Enumerate executable non-generated source methods in test projects and obtain roots through `TestFrameworkClassifier.IsActiveTestRoot`; do not reimplement its framework rules. Build and traverse the solution call graph with the edge types and intermediate code specified in the concept. Include callbacks/local functions as potential edges of their containing reached function, cross-project helpers, private functions, cycles, and source-generated intermediates. Preserve direct-path precedence and a deterministic shortest indirect path. Track method groups, unresolved binding, and virtual/interface dispatch as specified uncertainty; do not cap traversal depth.
  - Not: Runtime dispatch guesses, reflection resolution, branch-execution claims, or duplicate test-root detection.
  - Acceptance: Focused tests demonstrate that recognized versus skipped roots from the shared classifier affect paths as specified; they also cover direct test-helper paths, indirect production-chain paths, direct-path precedence, deterministic shortest-path tie breaking, callback, constructor/accessor/operator, cross-project, cyclic, and generated-code paths; no path from names/comments or an unexecuted helper; and affected/global uncertainty without a false resolved path. A required missing semantic model fails rather than returning partial data. Run affected FastTests and the build gate before committing.

- [ ] **3. Review-analysis findings and options**
  - Intention: Turn zero-path and sufficiently complex indirect-only functions into distinctly labeled review findings.
  - Scope: Implement `missing-test-evidence-candidates` behavior version 1 using points 1–2, with descriptor defaults and validation for the four positive integer thresholds, category selection, symbol identity, evidence, numeric metrics, compact signals, indirect-path data, review questions, and deterministic ordering. Keep the analysis unregistered until point 4 so it cannot publish with the old changed-files behavior.
  - Not: Build diagnostics, test generation, minimum test counts, suppressions, or changes to other analysis thresholds.
  - Acceptance: FastTests cover exact JSON option names and defaults from the concept, omitted options, invalid types and values, one finding per eligible zero-path or indirect-only function, exclusion below each category's gate, suppression by a single direct path, uncertainty wording, complete empty results, repeat runs, cancellation, and validation-compatible evidence. Run affected FastTests and the build gate before committing.

- [ ] **4. Host registration, baseline selection, and current-state documentation**
  - Intention: Publish the complete analysis through the normal CLI without hiding newly missing-test findings from the primary report view.
  - Scope: Register and enable the analysis by default; update the repository example config and generated defaults. Render both finding categories and one shortest indirect path in Markdown. Apply its C#-snapshot-wide `changed-files` selection without changing other analyses' file-based selection; explain the exception in the report index. Update README and affected `docs/` pages with verified behavior.
  - Not: Baseline schema changes, dynamic analysis loading, or a new CLI mode.
  - Acceptance: Host/config/report tests verify that generated defaults and the repository example use the exact configuration entry in the concept; they also cover valid and invalid config, category labels and the selected indirect path, no baseline, unchanged baseline, added/changed/deleted C# paths, non-C#-only changes, full `all-findings`, empty results, repeated runs, and no publication on analysis failure/cancellation. Run FastTests, IntegrationTests, build, documentation diff review, and `git diff --check` before committing.

- [ ] **5. Final audit**
  - Intention: Confirm the completed work matches the ready concept and works as a whole.
  - Scope: Review the implementation, test evidence, configuration, both report views, docs, and git diff against every Must/Not clause in [Konzept.md](Konzept.md). Run the required project gates and record concrete gaps for one correction pass if needed.
  - Not: New feature scope or speculative refactoring.
  - Acceptance: Every concept clause has implementation and verification evidence; no relevant test is weakened; all affected gates pass; docs describe only verified current behavior; and the roadmap checkboxes and commits accurately reflect completed slices.
