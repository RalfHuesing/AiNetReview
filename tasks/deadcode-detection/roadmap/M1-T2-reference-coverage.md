# M1-T2 — Shared semantic reference coverage

## Intention

Provide reusable, solution-wide Roslyn reference information and explicit completeness signals for rules, while keeping dead-code candidate decisions inside the later rule.

## Scope

- Build shared analysis helpers over the loaded Roslyn snapshot for symbol identity, references, source/project classification and reference roles that the dead-code rule needs. Include references from test projects, method groups and generated C# code; preserve the distinction between a member's use and a type's own self-reference.
- Expose enough provenance for later candidate decisions and symbol-specific uncertainty. A globally incomplete reference enumeration fails the run; symbol-local unresolved binding is represented so the later rule can suppress only affected candidates.
- Reuse or extend `ReviewSourceClassifier` and existing context services. Make generally useful helpers available to current rules without changing their observable findings or adding a rule base class.

## Non-goals

Dead-code candidate filtering, markup parsing, MCP continuation/budget behavior, and changes to AiNetLinter.

## Contracts and invariants

- Analyze the already loaded solution and compilations; do not reread source files during rule execution.
- Distinguish complete zero references from unknown coverage. Never convert a failed or incomplete enumeration into a complete empty result.
- The relevant read-only mechanisms and behavior tests are listed in [Konzept.md](../Konzept.md).

## Acceptance

- [x] Focused tests demonstrate references across production and test projects, method-group references, generated C# references, and self-reference provenance.
- [x] Tests demonstrate global coverage failure versus symbol-local uncertainty and preserve the existing rule's results.

## Checklist

- [x] Inspect the loaded-solution, classifier and runner contracts and the relevant AiNetLinter reference tests.
- [x] Implement this scope and update affected current-state documentation in the same slice.
- [x] Run focused tests and required project gates; review the diff and `git diff --check`.
- [x] Close this leaf and its parent roadmap checkbox only after acceptance is verified; commit explicit task paths.

## Abschlussnachweis

`SolutionReferenceIndex` indexes references from loaded C# documents and source-generated documents, with project role, generated-source, method-group, enclosing-symbol and self-reference provenance. Globally unavailable project/document semantic coverage throws `AnalysisFailedException`; unresolved candidate bindings stay local to affected symbols. FastTests cover complete zero-reference results and unchanged output from the existing rule. The solution build, full FastTests and IntegrationTests passed before commit.
