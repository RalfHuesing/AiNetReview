# Command-line host

The executable accepts one command: `ainetreview review --config <absolute-path-to-ainetreview.json>`. The option must identify an `ainetreview.json` file directly under the project root. The command synchronously validates the configuration, loads and checks the configured solution, runs its configured rules, and publishes the complete Markdown report set.

On success, stdout contains exactly one compact JSON line, for example:

```json
{"status":"completed","runId":"20260928T163802Z-a1b2c3d4","indexPath":"audit-reporting/20260928T163802Z-a1b2c3d4/index.md","counts":{"detected":0}}
```

`indexPath` is relative to the configuration's project root. A complete run with findings also exits with code `0`. Errors produce one `{"code":"...","message":"..."}` JSON line on stderr and no success response on stdout. Exit codes are `2` for `INVALID_INPUT`, `3` for `ANALYSIS_FAILED`, `4` for `REPORT_FAILED` or `LOGGING_FAILED`, and `130` for `CANCELLED`. Parser errors use `INVALID_INPUT`; progress and log events are never written to either process stream.

Serilog initializes before parsing. Its only sink writes to `<AppContext.BaseDirectory>/logs/`, independent of the current directory, configuration path, and project root. The daily file rotates at 10 MiB, keeps at most 30 files, and supports concurrent processes. Log events include command, run ID when available, and failure context; configuration contents and source text are not logged. If logging cannot start, the process returns `LOGGING_FAILED` with code `4`.

The adapter accepts injected services and input/output streams. IntegrationTests use this same adapter with a test-only fixture rule; the executable composition root registers `method-control-flow-outliers`.
