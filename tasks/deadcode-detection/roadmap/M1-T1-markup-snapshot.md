# M1-T1 — Conditional markup snapshot

## Intention

Make relevant non-C# project content available to review rules as part of the same immutable loaded-solution view, without reading it again during rule execution.

## Scope

- Extend `SolutionLoader`, `LoadedSolution` and the rule context as needed to capture `.razor`, `.xaml` and `.js` content from analyzed C# projects when `dead-code-candidates` is configured. Include project files that Roslyn does not expose as `AdditionalDocuments`; preserve a stable path/content view through later file changes.
- Bound discovery to each project root without following reparse points. Exclude build output, dependency directories and nested foreign projects. Apply the fixed AiNetLinter safety limits of 2,000 markup files total and 1 MiB per file.
- Convert unreadable files, discovery or path uncertainty, and exceeded limits into `AnalysisFailedException` or the established analysis-failure path. Do not return a partial snapshot. Preserve the current behavior and cost for configurations without this rule.

## Non-goals

Markup binding analysis and candidate selection; changes to the AiNetLinter repository; a general file-watching or cache service.

## Contracts and invariants

- The snapshot is complete before any rule runs and cannot change because a disk file changes afterward.
- Every captured path belongs to an analyzed project and the configured project root. The existing C# source-boundary checks remain in force.
- Use the existing project-path and source-classification infrastructure where applicable. See [Konzept.md](../Konzept.md) for the complete discovery and failure boundaries.

## Acceptance

- [x] Integration tests cover an included file that is not a Roslyn `AdditionalDocument`, a file changed after load, conditional capture, path and reparse boundaries, skipped directories, unreadable files, and both size limits.
- [x] Snapshot failures produce no successful review result or published report; configurations without `dead-code-candidates` need no markup capture.

## Checklist

- [x] Inspect current loader, context, configuration and relevant AiNetLinter markup behavior.
- [x] Implement this scope and update affected current-state documentation in the same slice.
- [x] Run focused tests and required project gates; review the diff and `git diff --check`.
- [x] Close this leaf after acceptance is verified; keep the parent milestone open until its sibling leaf and audit are complete, then commit explicit task paths.
