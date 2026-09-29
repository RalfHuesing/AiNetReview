# Roadmap: Missing test evidence candidates

The contract is [Konzept.md](Konzept.md). Complete the points in order. Each implementation point includes its own focused tests, affected project gates, documentation check, checkbox update, and atomic commit. Do not change production behavior without an automated contract test.

- [ ] **1. Candidate enumeration and control-flow measurement**
  - Intention: Produce a deterministic set of structurally nontrivial production functions without changing the existing outlier analysis's results.
  - Scope: Reuse or extract the current decision walker; cover the function kinds, exclusions, partial/expression bodies, and both pairs of complexity gates in the concept. Keep local functions and lambdas out of candidate measurements.
  - Not: Test attribution, registration, or report publication.
  - Acceptance: Focused tests exercise every candidate kind, both threshold pairs, their OR/AND combination and reversed configured thresholds, flat switch/grouped labels, nesting and `else if`, generated/test exclusions, and unchanged control-flow-outlier behavior. Run affected FastTests and the build gate before committing.

- [ ] **2. Recognized test roots**
  - Intention: Identify methods that a standard unfiltered test run could enter, rather than treating a test project or matching name as proof.
  - Scope: Classify roots by the xUnit, NUnit, and MSTest semantic attributes and inheritance rules in the concept; handle static whole-method/fixture skip or explicit metadata and parameterized methods. Keep the classifier independent of call-path traversal.
  - Not: Naming, `typeof`, `nameof`, comments, dynamic condition evaluation, or data-source execution as evidence.
  - Acceptance: Tests cover each supported root family, derived/custom xUnit v3 attributes, MSTest's containing `TestClass`, one root per parameterized method, excluded static skip/ignore/explicit cases, and ordinary helpers or names without test attributes. Run affected FastTests and the build gate before committing.

- [ ] **3. Semantic test-origin paths and uncertainty**
  - Intention: Distinguish direct, indirect, and absent static paths from recognized test roots without claiming runtime coverage.
  - Scope: Build and traverse the solution call graph with the edge types and intermediate code specified in the concept. Include callbacks/local functions as potential edges of their containing reached function, cross-project helpers, private functions, cycles, and source-generated intermediates. Preserve direct-path precedence and a deterministic shortest indirect path. Track method groups, unresolved binding, and virtual/interface dispatch as specified uncertainty; do not cap traversal depth.
  - Not: Runtime dispatch guesses, reflection resolution, or branch-execution claims.
  - Acceptance: Tests demonstrate direct test-helper paths, indirect production-chain paths, a direct path winning over an indirect one, deterministic shortest-path tie breaking, callback, constructor/accessor/operator, cross-project, cyclic, and generated-code paths; no path from names/comments or an unexecuted helper; and affected/global uncertainty without a false resolved path. A required missing semantic model fails rather than returning partial data. Run affected FastTests and the build gate before committing.

- [ ] **4. Review-analysis findings and options**
  - Intention: Turn zero-path and sufficiently complex indirect-only functions into distinctly labeled review findings.
  - Scope: Implement `missing-test-evidence-candidates` behavior version 1 using points 1–3, with descriptor defaults and validation for the four positive integer thresholds, category selection, symbol identity, evidence, numeric metrics, compact signals, indirect-path data, review questions, and deterministic ordering. Keep the analysis unregistered until point 5 so it cannot publish with the old changed-files behavior.
  - Not: Build diagnostics, test generation, minimum test counts, suppressions, or changes to other analysis thresholds.
  - Acceptance: FastTests cover default/configured/invalid thresholds, one finding per eligible zero-path or indirect-only function, exclusion below each category's gate, suppression by a single direct path, uncertainty wording, complete empty results, repeat runs, cancellation, and validation-compatible evidence. Run affected FastTests and the build gate before committing.

- [ ] **5. Host registration, baseline selection, and current-state documentation**
  - Intention: Publish the complete analysis through the normal CLI without hiding newly missing-test findings from the primary report view.
  - Scope: Register and enable the analysis by default; update the repository example config and generated defaults. Render both finding categories and one shortest indirect path in Markdown. Apply its C#-snapshot-wide `changed-files` selection without changing other analyses' file-based selection; explain the exception in the report index. Update README and affected `docs/` pages with verified behavior.
  - Not: Baseline schema changes, dynamic analysis loading, or a new CLI mode.
  - Acceptance: Host/config/report tests cover valid and invalid config, category labels and the selected indirect path, no baseline, unchanged baseline, added/changed/deleted C# paths, non-C#-only changes, full `all-findings`, empty results, repeated runs, and no publication on analysis failure/cancellation. Run FastTests, IntegrationTests, build, documentation diff review, and `git diff --check` before committing.

- [ ] **6. Final audit**
  - Intention: Confirm the completed work matches the ready concept and works as a whole.
  - Scope: Review the implementation, test evidence, configuration, both report views, docs, and git diff against every Must/Not clause in [Konzept.md](Konzept.md). Run the required project gates and record concrete gaps for one correction pass if needed.
  - Not: New feature scope or speculative refactoring.
  - Acceptance: Every concept clause has implementation and verification evidence; no relevant test is weakened; all affected gates pass; docs describe only verified current behavior; and the roadmap checkboxes and commits accurately reflect completed slices.
