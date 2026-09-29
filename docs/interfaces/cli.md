# Command-line host

The executable requires one of two subcommands:

- `ainetreview review [project-path]` runs the review.
- `ainetreview baseline [project-path]` captures a source baseline without running review analyses.

The optional project path defaults to the current working directory and may be relative or absolute. Both commands always use `ainetreview.json` directly under the selected project root. Calls without a subcommand, unknown subcommands, and unknown options return `INVALID_INPUT` with exit code `2`. The legacy `--cmd` and `--config` options are not accepted.

When `ainetreview.json` does not exist, either command searches only the project root's top level for `.slnx` and `.sln` files (extensions are matched case-insensitively), generates the configuration from the registered analyses' defaults, and continues with that file. Solution selection prefers a filename stem matching the project directory name (case-insensitively), then `.slnx`, then ordinal filename order. For example, in a directory named `Project`, `Project.sln` wins over `Other.slnx`; without a name match, an `.slnx` wins over any `.sln`, and ties are resolved by ordinal filename order. If no solution is found, the command returns `INVALID_INPUT` with exit code `2` and does not create a configuration file.

The `review` command validates the configuration, loads and checks the configured solution, runs its configured analyses, and publishes the complete Markdown report set. The `baseline` command uses the same configuration and loaded source snapshot but does not run review analyses or publish a report. It writes `baseline.json` directly in the configured output directory, containing each source file's project-relative path and lowercase SHA-256 hash, including files without findings. It includes generated-source-filtered C# documents and, when `dead-code-candidates` is configured, the captured `.razor`, `.xaml`, and `.js` files. A later baseline replaces the complete file set, so deleted files disappear. The replacement uses a temporary file in the output directory and a final file rename; a failed load, write, or cancellation keeps the previous baseline.

On review success, stdout contains exactly one compact JSON line, for example:

```json
{"status":"completed","runId":"20260928T163802Z-a1b2c3d4","indexPath":"audit-reporting/20260928T163802Z-a1b2c3d4/index.md","counts":{"detected":0}}
```

`indexPath` is relative to the configuration's project root. It links to separate `changed-files/` and `all-findings/` views, directly links every analysis report file that exists, and includes a quoted PowerShell baseline command. A normal run uses the running executable and the target configuration. A centrally hosted audit embeds its effective target configuration in the central output directory and names the actual AiNetReview executable, target root, and output directory. A complete run with findings also exits with code `0`. Errors produce one `{"code":"...","message":"..."}` JSON line on stderr and no success response on stdout. Exit codes are `2` for `INVALID_INPUT`, `3` for `ANALYSIS_FAILED`, `4` for `REPORT_FAILED` or `LOGGING_FAILED`, and `130` for `CANCELLED`. Parser errors use `INVALID_INPUT`; progress and log events are never written to either process stream.

Baseline success also exits with code `0` and writes one JSON line containing `status`, `baselinePath` (relative to the project root), and `files`. Baseline write failures return `BASELINE_FAILED` with exit code `4`; input, load, and cancellation errors use the same codes as review.

Serilog initializes before parsing. Its only sink writes to `<AppContext.BaseDirectory>/logs/`, independent of the current directory, configuration path, and project root. The daily file rotates at 10 MiB, keeps at most 30 files, and supports concurrent processes. Log events include command, run ID when available, and failure context; configuration contents and source text are not logged. If logging cannot start, the process returns `LOGGING_FAILED` with code `4`.

The adapter accepts injected services and input/output streams. IntegrationTests use this same adapter with a test-only fixture analysis; the executable composition root registers the production analyses `method-control-flow-outliers`, `dead-code-candidates`, and `duplicate-code-candidates`.
