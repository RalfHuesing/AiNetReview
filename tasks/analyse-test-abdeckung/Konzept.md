---
status: draft
---

# Test evidence for nontrivial production functions

## Intention

Autonomous changes need deterministic tests as feedback. The review should identify nontrivial C# functions for which the loaded solution offers no statically attributable executable test, so a reviewer or agent can decide where a useful test is missing. A finding is an investigation prompt, not a claim that the function is untested at runtime or that a particular test is good.

## Scope

### Must

- Add one report-only review analysis for production C# functions. Use the existing solution snapshot, source classifier, analysis registry, configuration, baseline selection, and Markdown reporting contracts.
- Examine executable ordinary methods, constructors, and property accessors, including private members. Exclude test projects and generated documents or symbols with the shared classifier. Do not count local functions and lambdas as separate subjects; attribute their control flow to the containing function only when it belongs to that function's execution.
- Select structurally nontrivial functions when either the decision count reaches a configurable minimum (proposed default: 3) or decision nesting reaches a configurable minimum (proposed default: 2). Reuse the existing control-flow definitions where they fit, including switch arms and grouped labels. Report both raw values and the effective thresholds as finding data. Method length alone is not a gate.
- Identify executable test methods in recognized test projects by test-framework semantics. Attribute a function to a test when a semantically resolved call or method reference occurs directly in that test method or in a test helper reached from it. Count distinct originating test methods, not assertions or files. A mere test-class name, `typeof`, `nameof`, comment, or reference from an unexecuted helper is not test evidence. Do not claim that a static call path proves execution of any branch.
- Report functions with zero such test origins, with source location, stable symbol identity, structural measurements, and a review question about which behavior and failure paths warrant tests. If only a production entry point called by a test reaches the function, make that limitation clear: the function may be covered indirectly even though no test origin is attributable under this rule.
- Keep the analysis informational. Findings do not fail builds, trigger refactoring, or require tests mechanically. Do not add source-comment suppressions, `// disabled`, `@covers`, or equivalent evidence markers. The normal per-analysis `enabled` configuration remains available.
- Handle unresolved or unsupported bindings explicitly: a missing proof of test association must not silently become a confident zero-test claim. State the uncertainty in the finding or skip the affected subject with a documented reason; the implementation contract must settle which before `ready`.

### Not

- Runtime coverage instrumentation, executing the target's tests, evaluating assertion quality, or proving that happy, error, and boundary cases are covered.
- A fixed requirement for two or more tests per function, a test-count score, or automatic test generation. The report can show the number of attributable tests for candidate reasoning without judging test adequacy.
- One-to-one adoption of AiNetLinter's class-level `StaticTestSentinel`, naming, `typeof`, or comment-based coverage and exemptions.
- New build-breaking analyzer diagnostics or changes to the existing control-flow-outlier analysis.

## Verification

- Focused analysis tests should cover nontrivial selection and thresholds; direct test calls, test-helper calls, method groups, names/comments without calls, multiple test origins, private functions, generated/test-project exclusions, and unresolved bindings.
- Host-level tests should verify configuration validation and generated defaults, report-only output and source identity, empty results, repeat runs after source/test changes, and no publication after analysis failure or cancellation.
- Run the affected FastTests and IntegrationTests and the required project gates. Update current-state documentation only with behavior verified after implementation.

## Working memory (draft only)

- Existing AiNetReview `method-control-flow-outliers` measures `decisionCount` and `maxDecisionNesting` for ordinary methods; the source classifier recognizes test projects and generated C#; `SolutionReferenceIndex` records production/test references and symbol-local binding uncertainty. It does not itself identify executable test origins. See `docs/review/findings.md` and `docs/development/adding-review-analyses.md`.
- AiNetLinter's `StaticTestSentinel` is class-level and accepts naming, `typeof`, and `@covers`; its MCP test context also distinguishes direct member invocations from weaker evidence. This task deliberately uses stronger function-level evidence and no comment-based exemptions.
- Decision pending: should the first analysis report only zero attributable test origins, or also functions with some tests but many decision paths? Recommendation: zero only. Static metrics cannot establish which paths a test exercises, so a minimum test count would create an arbitrary adequacy claim. A reviewer can inspect reported complexity and add multiple tests where justified.
- Decision pending: when one function has unresolved test-side bindings, should the report include an explicitly uncertain candidate or omit it? Recommendation: include it with clear uncertainty so the reviewer can investigate without treating absence as proof.

## Original request

Im Schwester Projekt AiNetLinter haben wir etwas wie test abdeckung.

das soll nicht 1:1 hier her übernommen werden aber so grob vom konzept her.
wir willen hier keine // disabled dinge haben.

grundsätzlich sollte diese analyse ermitteln welche funktionen nicht trivial sind und KEINE tests haben.
wie genau wir das messen können und welche konfigurierbaren metriken wir brauchen müsstest du entscheiden und dir überlegen.

tests sind essenziell für vollautonome agentische entwicklung da sie ein deterministisches ergebnis liefern.

ob der test sinnvoll ist können wir - vermute ich - per roslyn nciht feststellen.
das wäre auch nicht der task.

vielleicht sollte es mehrere tests geben?
nicht nur happy path tests?

wir reporten das ja nur und der agent muss entscheiden was unsinn ist und was man davon umsetzen sollte.
