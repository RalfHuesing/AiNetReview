# Review configuration

The Core accepts one `ainetreview.json` file directly below the project root. `ReviewConfigValidator.Load` requires an absolute path to a file with that exact name. The project root is the file's parent directory and does not need to be a Git repository.

Schema version 1 currently accepts these fields:

```json
{
  "schemaVersion": 1,
  "solution": "Project.slnx",
  "outputDirectory": "audit-reporting",
  "rules": {
    "method-control-flow-outliers": {
      "enabled": true,
      "percentile": 90
    },
    "dead-code-candidates": {
      "enabled": true,
      "apiSurface": "external_library",
      "entryPointAttributes": []
    },
    "duplicate-code-candidates": {
      "enabled": true,
      "minTokens": 30,
      "minimumSimilarity": "exact"
    }
  }
}
```

The root object must contain exactly `schemaVersion`, `solution`, `outputDirectory`, and `rules`. `schemaVersion` must be the integer `1`. The solution and output paths are nonempty project-relative paths using `/` separators; backslashes are rejected. The solution must resolve to an existing `.sln` or `.slnx` file, and the output directory must stay inside the project root. The validator creates the output directory when needed.

The repository root contains an `ainetreview.json` that selects `AiNetReview.slnx`, writes reports to `audit-reporting/`, and lists every production rule. It explicitly enables all three rules, sets the `method-control-flow-outliers` percentile to its default of 90, and selects the low-noise `exact` duplicate similarity with its default 30-token minimum. The normal IntegrationTests suite invokes the Debug executable with this absolute configuration path and verifies the generated run in the repository output directory.

Separately started repository audits use profiles under `audit-targets/`, described in [Build and Tests](../development/build-and-tests.md). These profiles select a repository, solution, and rules while keeping generated reports under the AiNetReview repository's `audit-reporting/<target>/` directory. Their optional top-level `enabled` boolean defaults to `true`; `false` skips the target audit. They are consumed by the opt-in integration test and do not change the normal `ainetreview.json` CLI contract.

`rules` must be a nonempty object. Each key must name a registered rule, and its value must be an object containing the optional boolean `enabled` field and only options declared by that rule. `enabled` defaults to `true`; `false` excludes the rule from analysis and report publication. All configured rules may be disabled. Options are validated even when a rule is disabled. The registry descriptor applies defaults to omitted rule-specific options and validates supplied value types. The production registry contains `method-control-flow-outliers`, `dead-code-candidates`, and `duplicate-code-candidates`. The first rule's optional `percentile` integer accepts values from 50 through 99 and defaults to 90. For `dead-code-candidates`, `apiSurface` accepts `external_library` (default) or `closed_solution`; `entryPointAttributes` is an array of additional fully qualified attribute type names and defaults to an empty array. Its fixed module initializer and JS interop attributes remain active. `duplicate-code-candidates` accepts a positive integer `minTokens` (default 30) and `minimumSimilarity` of `exact` (default, 0.95), `near` (0.80), or `fuzzy` (0.65). Each complete audit includes only current findings in its rule reports; rules with no findings have no report file. An index with no active rules states that no review ran; an active audit without findings states that no findings were found.

Unknown or duplicate JSON keys, unknown rule IDs, invalid option values, wrong field types, unsupported schema versions, paths escaping through `..`, symlinks, or junctions, and missing solution files raise `InvalidReviewInputException`. A loaded solution that includes C# source outside the project root or inside the output directory is rejected, except for generated external source files included by test projects; those files are excluded from the review snapshot. Each C# project must produce a Roslyn compilation without compiler errors; warnings promoted to errors by a target project's build policy do not block review. Solution load, restore, missing reference, or compilation failures raise `AnalysisFailedException`.

`SolutionLoader` uses `MSBuildWorkspace` to load `.sln` and `.slnx` files and does not invoke Git. The `review --config` host command invokes the validator, loader, rule runner, and Markdown report writer in sequence. A complete run publishes a report directory; handled input, analysis, cancellation, or report failures do not return a success response.
