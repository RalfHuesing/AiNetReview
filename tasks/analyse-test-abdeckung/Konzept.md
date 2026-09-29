---
status: ready
---

# Missing test evidence candidates

## Intention

Tests provide deterministic feedback for autonomous changes. This analysis identifies structurally nontrivial production C# functions for which the loaded solution contains no statically resolved call path from a recognized test method. A finding asks a reviewer to investigate useful tests; it never claims that runtime coverage is zero or that an existing test checks the right behavior.

## Scope

### Must

- Add one enabled-by-default, report-only review analysis with the stable ID `missing-test-evidence-candidates` and behavior version `1`. Use the existing solution snapshot, shared production/test and generated-source classifier, analysis registry, configuration validation, baseline, and Markdown report contracts. Emit one finding per eligible function with no attributed test path.
- Eligible functions are explicit executable ordinary methods, constructors, property and indexer getters/setters, event add/remove accessors, and user-defined operators/conversions in production C# projects, regardless of accessibility. Include expression bodies and the implementation part of partial methods. Exclude declarations without a body and generated documents or symbols. Local functions and lambdas are neither separate candidates nor part of a containing function's complexity measurement.
- Measure a function body with the existing control-flow definitions: `decisionCount` counts each `if`, switch section or expression arm, conditional expression, loop, and `catch`; grouped switch labels count as one section. `maxDecisionNesting` is the deepest level of these decisions, with an `else if` chain at one level. A function is nontrivial when `decisionCount >= minDecisionCount` **or** `maxDecisionNesting >= minDecisionNesting`. Defaults are `3` and `2`; both options accept JSON integers from `1` through `Int32.MaxValue`. Length, name, and type size are not selection gates.
- Recognize test roots only in projects classified as tests. Roots are source methods carrying xUnit `Fact`/`Theory` (including derived attributes and xUnit v3 `IFactAttribute` implementations), NUnit `Test`, `TestCase`, or `TestCaseSource`, or MSTest `TestMethod`/`DataTestMethod` inside a `TestClass`, including derived attributes. One parameterized method is one root regardless of its data rows. Do not use class names, method names, `typeof`, `nameof`, comments, or mere membership in a test project as test evidence. Exclude a root when static framework metadata marks the entire method or fixture skipped or explicit for an ordinary unfiltered test run (`Skip`, `Ignore`, or `Explicit`); do not attempt to evaluate dynamic conditions or data sources.
- Build a possible-call graph from the loaded C# solution and traverse it from every recognized test root without an arbitrary depth limit. An edge exists for a semantically resolved invocation, object creation, getter/setter or event accessor use, or user-defined operator/conversion call. Traverse through test helpers and production functions, including private functions and source-generated intermediate code. Calls syntactically inside a reached function's lambda or local function are possible edges of that function; this is deliberately conservative because the callback may never run. A method-group reference alone is not an edge. For virtual/interface calls, follow the statically bound target only; do not invent runtime dispatch to implementations. A function with at least one path is treated as test-associated regardless of the number of roots or branches.
- Report a nontrivial function with no resolved path as `no static test path`, not `no tests`. Mark the finding `attribution uncertain` when a reachable unresolved binding, method group, or virtual/interface dispatch could target that function. If an unresolved binding cannot be narrowed to affected functions, mark every otherwise reportable function uncertain. Reflection, dependency injection, external tests, dynamic dispatch, runtime branch execution, and custom test discovery can also hide associations; explain these general limits in the report. Failure to obtain a required compilation or semantic model fails the analysis rather than publishing partial results.
- Give each finding a stable symbol identity and declaration location, using the existing DocId-with-qualified-name-fallback convention. Store raw decision values and effective thresholds in finding metrics, and show those values plus the uncertainty label in the compact Markdown signal. Ask the reviewer which behavior, boundary, and failure paths deserve tests. The analysis must not assert a minimum number of tests, prescribe happy/error-path counts, fail a build, generate tests, or refactor code.
- Preserve the complete `all-findings` view. Because absence of a test path depends on the whole C# snapshot, apply a conservative rule only to this analysis in `changed-files`: with no baseline, show every finding; with a baseline, show every current finding from this analysis if any C# snapshot path was added, changed, or deleted, and none if no C# snapshot path changed. Explain this analysis-specific selection rule in the report index. Other analyses retain their existing file-based selection.
- Expose only `enabled`, `minDecisionCount`, and `minDecisionNesting` as configuration controls. Do not add source-comment suppressions, `// disabled`, `@covers`, exemption lists, or per-function overrides. Update the repository example configuration and current-state documentation when implemented.

### Not

- Runtime instrumentation or execution of the target tests; proof of coverage, assertion quality, test success, or which paths run.
- A finding for functions with one or more static test paths solely because their decision count exceeds their test count. One possible path is enough to remove a function from this analysis; test sufficiency remains a reviewer decision.
- AiNetLinter's class-level `StaticTestSentinel` behavior, naming/`typeof`/comment matches, or its exemptions.
- Build-breaking diagnostics, changes to existing analysis thresholds, and automatic source or test edits.

## Verification

- Analysis tests cover each candidate kind, both selection thresholds and their boundary values, skipped/generated/test-project exclusions, test roots across the supported frameworks, direct and transitive paths, private methods, cycles, lambdas, method groups, virtual/interface dispatch, and uncertain bindings. Test names and comments without calls must not suppress a finding.
- Configuration and host tests cover defaults and invalid values, deterministic finding identity and compact signals, empty results, repeated runs after test and production edits, analysis failure/cancellation without publication, and the special `changed-files` behavior for added, changed, and deleted C# files with and without a baseline.
- Run the affected FastTests, IntegrationTests, build gate, documentation review, and `git diff --check` before completion. Update `docs/` and the repository README to describe verified current behavior.
