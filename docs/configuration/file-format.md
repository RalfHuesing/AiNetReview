# Review configuration

The Core accepts one `ainetreview.json` file directly below the project root. `ReviewConfigValidator.Load` requires an absolute path to a file with that exact name. The project root is the file's parent directory and does not need to be a Git repository.

Schema version 1 currently accepts these fields:

```json
{
  "schemaVersion": 1,
  "solution": "Project.slnx",
  "outputDirectory": "audit-reporting",
  "rules": {
    "method-control-flow-outliers": {}
  }
}
```

The root object must contain exactly `schemaVersion`, `solution`, `outputDirectory`, and `rules`. `schemaVersion` must be the integer `1`. The solution and output paths are nonempty project-relative paths using `/` separators; backslashes are rejected. The solution must resolve to an existing `.sln` or `.slnx` file, and the output directory must stay inside the project root. The validator creates the output directory when needed.

The repository root contains an `ainetreview.json` that selects `AiNetReview.slnx`, writes reports to `audit-reporting/`, and enables `method-control-flow-outliers` with its default percentile of 90. The normal IntegrationTests suite invokes the Debug executable with this absolute configuration path and verifies the generated run in the repository output directory.

`rules` must be a nonempty object. Each key must name a registered rule, and its value must be an object containing only options declared by that rule. The registry descriptor applies defaults to omitted options and validates supplied value types. The production registry contains `method-control-flow-outliers`; its optional `percentile` integer accepts values from 50 through 99 and defaults to 90.

Unknown or duplicate JSON keys, unknown rule IDs, invalid option values, wrong field types, unsupported schema versions, paths escaping through `..`, symlinks, or junctions, and missing solution files raise `InvalidReviewInputException`. A loaded solution that includes C# source outside the project root or inside the output directory is rejected. Each C# project must produce a Roslyn compilation without error diagnostics; solution load, restore, missing reference, or compilation failures raise `AnalysisFailedException`.

`SolutionLoader` uses `MSBuildWorkspace` to load `.sln` and `.slnx` files and does not invoke Git. The `review --config` host command invokes the validator, loader, rule runner, and Markdown report writer in sequence. A complete run publishes a report directory; handled input, analysis, cancellation, or report failures do not return a success response.
