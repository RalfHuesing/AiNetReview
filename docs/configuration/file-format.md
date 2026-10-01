# Review configuration

The Core accepts one `ainetreview.json` file directly below the project root. `ReviewConfigValidator.Load` requires an absolute path to a file with that exact name. The project root is the file's parent directory and does not need to be a Git repository.

Schema version 1 currently accepts these fields:

```json
{
  "schemaVersion": 1,
  "solution": "Project.slnx",
  "outputDirectory": "audit-reporting",
  "analyses": {
    "method-control-flow-outliers": {
      "enabled": true,
      "percentile": 90,
      "testOptions": {
        "percentile": 90
      }
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
    },
    "indirection-drift-candidates": {
      "enabled": true
    },
    "missing-test-evidence-candidates": {
      "enabled": true,
      "minDecisionCount": 3,
      "minDecisionNesting": 2,
      "minIndirectDecisionCount": 5,
      "minIndirectDecisionNesting": 3
    },
    "code-size-candidates": {
      "enabled": true,
      "percentile": 90,
      "minMemberCodeLines": 80,
      "extremeMemberCodeLines": 300,
      "minTypeCodeLines": 300,
      "extremeTypeCodeLines": 800,
      "extremeFileLines": 1000,
      "extremeFileUtf8Bytes": 131072,
      "testOptions": {
        "percentile": 90,
        "minMemberCodeLines": 80,
        "extremeMemberCodeLines": 300,
        "minTypeCodeLines": 300,
        "extremeTypeCodeLines": 800,
        "extremeFileLines": 1000,
        "extremeFileUtf8Bytes": 131072
      }
    },
    "non-ascii-identifiers": {
      "enabled": true
    },
    "structural-duplication-candidates": {
      "enabled": true
    }
  }
}
```

The root object must contain exactly `schemaVersion`, `solution`, `outputDirectory`, and `analyses`. `schemaVersion` must be the integer `1`. The solution and output paths are nonempty project-relative paths using `/` separators; backslashes are rejected. The solution must resolve to an existing `.sln` or `.slnx` file, and the output directory must stay inside the project root. The validator creates the output directory when needed.

The repository root contains an `ainetreview.json` that selects `AiNetReview.slnx`, writes reports to `audit-reporting/`, and lists every production analysis. It explicitly enables all eight analyses, sets `method-control-flow-outliers` and `code-size-candidates` to their default percentile of 90, selects the low-noise `exact` duplicate similarity with its default 30-token minimum, and includes `missing-test-evidence-candidates` with all four defaults. The normal IntegrationTests suite invokes the Debug executable with the repository root as the project path and verifies the generated run in the repository output directory.

Both CLI commands always use `ainetreview.json` directly under the selected project root. If the file is missing, the CLI bootstraps it before loading it. The solution search checks only files directly inside the project root and recognizes `.slnx` and `.sln` extensions without regard to case. If several files match, it first prefers a filename stem equal to the project directory name (case-insensitively), then prefers `.slnx`, then chooses the ordinally first filename. No match returns `INVALID_INPUT` (exit code `2`) and leaves the config file absent.

The generated schema-v1 file uses `audit-reporting` as its output directory and includes every analysis in the host's `ReviewAnalysisRegistry`. It writes each analysis's `DefaultEnabled` value and every option's descriptor default, so the analysis descriptor is authoritative for generated defaults. For size and control-flow analyses it also writes each supported `testOptions` default from those same option descriptors. In the production CLI registry, all eight analyses default to enabled: `method-control-flow-outliers` uses `percentile: 90`; `code-size-candidates` uses `percentile: 90`, `minMemberCodeLines: 80`, `extremeMemberCodeLines: 300`, `minTypeCodeLines: 300`, `extremeTypeCodeLines: 800`, `extremeFileLines: 1000`, and `extremeFileUtf8Bytes: 131072`; `dead-code-candidates` uses `apiSurface: "external_library"` and `entryPointAttributes: []`; `duplicate-code-candidates` uses `minTokens: 30` and `minimumSimilarity: "exact"`; `missing-test-evidence-candidates` uses `minDecisionCount: 3`, `minDecisionNesting: 2`, `minIndirectDecisionCount: 5`, and `minIndirectDecisionNesting: 3`; `indirection-drift-candidates`, `non-ascii-identifiers`, and `structural-duplication-candidates` define no custom options. Analyses and options are emitted in ordinal name order. The generator returns formatted JSON; the CLI creates the file on disk. The CLI records file creation in its host log and does not write a bootstrap message to stdout or stderr.

Separately started repository audits use profiles under `audit-targets/`, described in [Build and Tests](../development/build-and-tests.md). These profiles select a repository, solution, and analyses while keeping generated reports under the AiNetReview repository's `audit-reporting/<target>/` directory. Their optional top-level `enabled` boolean defaults to `true`; `false` skips the target audit. They are consumed by the opt-in integration test and do not change the normal `ainetreview.json` CLI contract.

`analyses` must be a nonempty object. The legacy `rules` field is rejected as an unknown configuration field. Each key must name a registered analysis, and its value must be an object containing the optional boolean `enabled` field, the declared analysis options, and `testOptions` only for analyses that support it. `enabled` defaults to `true`; `false` excludes the analysis from execution and report publication. All configured analyses may be disabled. Options are validated even when an analysis is disabled. The registry descriptor applies defaults to omitted analysis-specific options and validates supplied value types. The production registry contains `code-size-candidates`, `method-control-flow-outliers`, `dead-code-candidates`, `duplicate-code-candidates`, `indirection-drift-candidates`, `missing-test-evidence-candidates`, `non-ascii-identifiers`, and `structural-duplication-candidates`. Both size and control-flow analyses accept `percentile` integers from 50 through 99 (default 90). `code-size-candidates` also accepts positive 32-bit integers `minMemberCodeLines` (80), `extremeMemberCodeLines` (300), `minTypeCodeLines` (300), `extremeTypeCodeLines` (800), `extremeFileLines` (1000), and `extremeFileUtf8Bytes` (131072). For `dead-code-candidates`, `apiSurface` accepts `external_library` (default) or `closed_solution`; `entryPointAttributes` is an array of additional fully qualified attribute type names and defaults to an empty array. Its fixed module initializer and JS interop attributes remain active. `duplicate-code-candidates` accepts a positive integer `minTokens` (default 30) and `minimumSimilarity` of `exact` (default, 0.95), `near` (0.80), or `fuzzy` (0.65). `structural-duplication-candidates` has no custom options; it uses fixed floors of three sibling statements and 60 original tokens, exact normalized syntax equality, and reports every occurrence with project, file, and end-exclusive coordinates. `missing-test-evidence-candidates` accepts four positive 32-bit integer thresholds: `minDecisionCount` (3), `minDecisionNesting` (2), `minIndirectDecisionCount` (5), and `minIndirectDecisionNesting` (3). `indirection-drift-candidates` and `non-ascii-identifiers` accept only the standard `enabled` option. `code-size-candidates.testOptions` supports the same seven options as the general configuration, and `method-control-flow-outliers.testOptions` supports `percentile`. Omitted test values inherit that analysis's effective general value; explicit test values are independent, and an empty object means complete inheritance. These values are validated even when an analysis is disabled. Other analyses reject `testOptions`. The repository example and generated files explicitly include every available option, including `testOptions` and array-valued defaults. Each complete audit includes only current findings in its analysis reports; analyses with no findings have no report file. An index with no active analyses says `No review was performed because all analyses are disabled`; an active audit without findings says `No findings were found`.

Unknown or duplicate JSON keys, unknown analysis IDs, invalid option values, wrong field types, unsupported schema versions, paths escaping through `..`, symlinks, or junctions, and missing solution files raise `InvalidReviewInputException`. A loaded solution that includes C# source outside the project root or inside the output directory is rejected, except for generated external source files included by test projects; those files are excluded from the review snapshot. Each C# project must produce a Roslyn compilation without compiler errors; warnings promoted to errors by a target project's build policy do not block review. Solution load, restore, missing reference, or compilation failures raise `AnalysisFailedException`.

`SolutionLoader` uses `MSBuildWorkspace` to load `.sln` and `.slnx` files and does not invoke Git. `review [project-path]` invokes the validator, loader, analysis runner, and Markdown report writer in sequence. `baseline [project-path]` invokes the same validator and loader, then writes `baseline.json` in the resolved output directory without running analyses or publishing a report. The file contains a complete set of project-relative paths and lowercase SHA-256 hashes for generated-source-filtered C# documents and any configured markup snapshot, including files without findings; replacing it also removes paths for deleted files. A temporary file and final rename preserve an existing baseline when loading or publication fails.

The centrally hosted manual audit builds its configuration from the selected `audit-targets/<name>.json` profile and writes reports under the AiNetReview repository's `audit-reporting/<name>/` directory. Its generated index links to `scripts/test-audit.ps1 -Target <name> -BaselineOnly` to update the centrally stored baseline; the normal CLI remains limited to `baseline [project-path]` and always uses the target's own `ainetreview.json`. See [Build and Tests](../development/build-and-tests.md) for the audit script.
