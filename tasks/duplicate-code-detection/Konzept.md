---
status: ready
---

# Duplicate Code Detection

## Intention

Make substantial, highly similar C# method bodies visible as current, solution-wide review candidates in AiNetReview. Reuse the tested token and clustering mechanisms from AiNetLinter while expressing the result through AiNetReview's rule, finding, configuration, and Markdown report contracts. A reviewer can inspect every participating source location and decide whether the duplication is intentional.

## Scope

### Must

- Add an explicitly registered production review rule named `duplicate-code-candidates`, list it in the repository configuration, and document its verified behavior in the affected current-state pages when implemented. It produces findings on every completed audit and an empty rule report when there are no candidates.
- Analyze the loaded, immutable C# solution snapshot across production projects, using AiNetReview's shared test-project and generated-source classifiers. Include executable ordinary methods, constructors, accessors, and local functions with block or expression bodies. Skip bodies below the minimum token count. Use project-root-relative paths and deterministic ordering.
- Adapt AiNetLinter's body-token, deterministic 5-token N-gram, inverted-index candidate generation, minimum 3 shared N-grams, Jaccard scoring, and union-find cluster mechanisms. Preserve identifier and literal text by default; ignore whitespace and comments as token trivia. Use a default minimum of 30 body tokens. Expose `minTokens` and `minimumSimilarity` as validated rule options; keep the other algorithm parameters fixed for this rule.
- `minimumSimilarity` accepts `exact`, `near`, or `fuzzy`, with `exact` as the low-noise default. These values select AiNetLinter's established Jaccard thresholds of 0.95, 0.80, and 0.65 respectively. A selected level includes all pairs at or above its threshold; the report states the effective threshold and each cluster's conservative score.
- Form reportable clusters only from pair edges meeting the configured threshold. This avoids losing an exact pair merely because a third method is connected by a weaker edge below the selected threshold. Produce one finding per cluster, anchored at a deterministic representative, with every member's method identity, project-relative source location, token count, and linked source-line evidence. Include the conservative cluster score and member count as numeric metrics. Explain that similarity alone does not prove the methods should be merged.
- Support members from different projects in one finding. Extend finding validation narrowly so evidence may reference any C# document in the loaded solution while retaining canonical path, line, snippet, and snapshot checks. Keep the finding's representative source owned by its project.
- Preserve cancellation and analysis-failure behavior: no partial successful result or published run. Tests must cover exact and non-exact pairs, transitive clusters, cross-project evidence, exclusions, deterministic identities/order, changed snapshots, empty runs, and failure/cancellation paths. Reuse relevant AiNetLinter test cases, adapted to AiNetReview's contracts.

### Not

- No build-breaking analyzer diagnostic, automatic extraction, or assertion that duplicate code is a defect.
- No structural-similarity reports, refactoring-drift search, MCP tool, or live dependency on AiNetLinter. Near/fuzzy token similarity is reported only when explicitly selected through `minimumSimilarity`.
- No port of AiNetLinter's file-comment suppression or `MaxResults` truncation. AiNetReview has no source-comment suppression contract, and each completed audit should expose all qualifying current candidates. The existing rule-level `enabled` setting remains available.

## Verification

- Focused rule tests compare representative positive and negative cases from AiNetLinter, including a near neighbor attached to an exact pair; assert one complete finding per qualifying cluster, correct default and configured similarity levels, validated options, and valid evidence for every member.
- Integration tests run the configured rule through the host and check Markdown links, complete and empty reports, cross-project members, cancellation, and absence of publication after failure. Run the affected project gates and review the documentation diff.
