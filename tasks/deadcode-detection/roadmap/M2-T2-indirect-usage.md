# M2-T2 — Indirect usage and suppression

## Intention

Protect declarations with recognized non-call-site bindings and explicit, justified local suppressions.

## Scope

- Extend the rule's candidate decision with recognized entry-point attributes, Reflection, DI, framework and `.razor`/`.xaml`/`.js` markup bindings, using only the loaded snapshot. Keep known indirect uses as protection signals and suppress the affected symbol when a relevant binding cannot be resolved safely.
- Support `entryPointAttributes` as an additional list of fully qualified attribute type names. Always retain `System.Runtime.CompilerServices.ModuleInitializerAttribute` and `Microsoft.JSInterop.JSInvokableAttribute`; compare actual symbols semantically, not by a simple name match.
- Support `// ainetreview-disable dead-code -- <reason>` immediately before a declaration as a suppression only for that declaration. A blank reason, a distant comment or a similarly named rule does not suppress it.
- Treat relevant markup that cannot be evaluated as incomplete coverage and fail the run, rather than publishing an apparently complete result.

## Non-goals

New project-role configuration, heuristic candidate confidence scores, AiNetLinter MCP output/caches, or a second full copy of its test matrix.

## Contracts and invariants

- A recognized indirect binding protects the bound symbol and, where applicable, its declaring type. Unresolved symbol-specific binding does not erase unrelated candidates.
- Markup safety and completeness boundaries are established by [M1-T1](M1-T1-markup-snapshot.md); behavior targets come from [Konzept.md](../Konzept.md).

## Acceptance

- [ ] Focused tests cover the fixed and configured entry-point attributes, semantic identity, representative Reflection/DI/framework and markup bindings, and valid/invalid suppression comments.
- [ ] Tests demonstrate that snapshot markup is used after disk changes, local uncertainty suppresses affected candidates, and unevaluable relevant markup fails the run.

## Checklist

- [ ] Inspect the snapshot interface and relevant AiNetLinter indirect/markup behavior and tests.
- [ ] Implement this scope and update affected current-state documentation in the same slice.
- [ ] Run focused tests and required project gates; review the diff and `git diff --check`.
- [ ] Close this leaf and its parent roadmap checkbox only after acceptance is verified; commit explicit task paths.
