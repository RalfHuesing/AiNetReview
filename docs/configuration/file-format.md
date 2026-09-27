# Review configuration

The Core accepts one `ainetreview.json` file directly below the project root. `ReviewConfigValidator.Load` requires an absolute path to a file with that exact name. The project root is the file's parent directory and does not need to be a Git repository.

Schema version 1 contains exactly these fields:

```json
{
  "schemaVersion": 1,
  "solution": "Project.slnx",
  "outputDirectory": "audit-reporting",
  "storageDirectory": ".ainetreview",
  "rules": {
    "template-noop": {}
  }
}
```

`schemaVersion` must be the integer `1`. All path fields are nonempty project-relative paths. `solution` must resolve to an existing `.sln` or `.slnx` file. Output and storage directories must resolve inside the project root and must be distinct, non-overlapping directories. The validator creates them when needed. Internal configuration paths are canonical project-relative paths using `/`; resolved absolute paths are kept separately for filesystem access.

`rules` must be a nonempty object. Each key must name a registered rule, and its value must be an object containing only options declared by that rule. The registry descriptor applies defaults to omitted options and validates supplied value types. The current production registry contains only `template-noop`, whose only valid configuration is `{}`.

Unknown or duplicate JSON keys, unknown rule IDs, invalid option values, wrong field types, unsupported schema versions, paths that escape through `..`, symlinks, or junctions, and missing solution files raise `InvalidReviewInputException`. A loaded solution that includes C# source outside the project root or inside an output or storage directory is rejected. Each C# project must produce a Roslyn compilation without error diagnostics; solution load, restore, missing reference, or compilation failures raise `AnalysisFailedException` rather than representing a successful review with no findings.

`SolutionLoader` uses `MSBuildWorkspace` to load `.sln` and `.slnx` files and does not invoke Git. These Core APIs are not yet connected to CLI or MCP commands.
