# M2-T1 — Candidate selection and direct usage

## Intention

Select only defensible type and ordinary-method review candidates from the complete solution reference view.

## Scope

- Implement the `dead-code-candidates` rule's candidate enumeration for explicit C# type and ordinary method declarations, including extension methods, in production projects. Apply the existing test-project and generated-source classification.
- Use the shared semantic reference information to count direct uses across the solution, including test and generated-code references and method groups. A member use protects its declaring type; self-references inside a type do not establish external type use. A type candidate groups its methods so they are not also emitted individually.
- Exclude interface contracts and implementations, overrides, compiler entry points, generated declarations and all declaration categories excluded by [Konzept.md](../Konzept.md). Apply `apiSurface` with `external_library` as the default and `closed_solution` as the second accepted value; publicly visible API is protected by default. Suppress a candidate affected by symbol-local unresolved binding and fail on globally incomplete reference coverage.
- Keep candidate policy inside the rule; shared helpers remain general analysis infrastructure.

## Non-goals

Indirect Reflection, DI or markup binding; suppression comments; automatic code edits; analyzer or build diagnostics.

## Contracts and invariants

- A complete lack of direct use alone is a review signal, not proof of dead code. The final rule result must obey the existing `IReviewRule` and runner failure contracts.
- Match the intended AiNetReview API policy in [Konzept.md](../Konzept.md), which differs from the AiNetLinter default.

## Acceptance

- [ ] Focused tests cover true type and method candidates, extension methods, method groups, test/generated uses, type grouping, self-reference behavior and excluded declaration categories.
- [ ] Tests cover both API policies and global versus local coverage uncertainty without copying the AiNetLinter test matrix.

## Checklist

- [ ] Inspect the current rule/option contracts and the relevant AiNetLinter candidate and API-policy behavior.
- [ ] Implement this scope and update affected current-state documentation in the same slice.
- [ ] Run focused tests and required project gates; review the diff and `git diff --check`.
- [ ] Close this leaf and its parent roadmap checkbox only after acceptance is verified; commit explicit task paths.
