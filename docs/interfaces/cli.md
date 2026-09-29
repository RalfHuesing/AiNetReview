# Command-line host

The executable accepts `ainetreview`, `ainetreview [project-path]`, `ainetreview review`, and `ainetreview review [project-path]`. Each form also accepts the optional `--config <path-to-ainetreview.json>` option. For example, `ainetreview`, `ainetreview review`, `ainetreview C:\src\Project`, and `ainetreview review C:\src\Project --config C:\src\Project\ainetreview.json` are valid. Relative project and config paths resolve against the current working directory. When no project path is given, the current working directory is used, except that a supplied config path determines the project root from its parent directory. A supplied configuration path must identify an `ainetreview.json` file directly under the selected project root.

When the configuration file does not exist, the host searches only the project root's top level for `.slnx` and `.sln` files (extensions are matched case-insensitively), generates `ainetreview.json` at the selected config path from the registered analyses' defaults, and immediately runs the review with that file. Solution selection prefers a filename stem matching the project directory name (case-insensitively), then `.slnx`, then ordinal filename order. For example, in a directory named `Project`, `Project.sln` wins over `Other.slnx`; without a name match, an `.slnx` wins over any `.sln`, and ties are resolved by ordinal filename order. If no solution is found, the command returns `INVALID_INPUT` with exit code `2` and does not create a configuration file.

The command validates the configuration, loads and checks the configured solution, runs its configured analyses, and publishes the complete Markdown report set.

On success, stdout contains exactly one compact JSON line, for example:

```json
{"status":"completed","runId":"20260928T163802Z-a1b2c3d4","indexPath":"audit-reporting/20260928T163802Z-a1b2c3d4/index.md","counts":{"detected":0}}
```

`indexPath` is relative to the configuration's project root. A complete run with findings also exits with code `0`. Errors produce one `{"code":"...","message":"..."}` JSON line on stderr and no success response on stdout. Exit codes are `2` for `INVALID_INPUT`, `3` for `ANALYSIS_FAILED`, `4` for `REPORT_FAILED` or `LOGGING_FAILED`, and `130` for `CANCELLED`. Parser errors use `INVALID_INPUT`; progress and log events are never written to either process stream.

Serilog initializes before parsing. Its only sink writes to `<AppContext.BaseDirectory>/logs/`, independent of the current directory, configuration path, and project root. The daily file rotates at 10 MiB, keeps at most 30 files, and supports concurrent processes. Log events include command, run ID when available, and failure context; configuration contents and source text are not logged. If logging cannot start, the process returns `LOGGING_FAILED` with code `4`.

The adapter accepts injected services and input/output streams. IntegrationTests use this same adapter with a test-only fixture analysis; the executable composition root registers the production analyses `method-control-flow-outliers`, `dead-code-candidates`, and `duplicate-code-candidates`.
