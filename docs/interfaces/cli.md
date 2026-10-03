# Command-line host

Every invocation uses this form:

```text
ainetreview review [project-path]
```

`review` runs every enabled configured analysis across the complete loaded solution and publishes one complete Markdown report. The executable provides standard CLI help and options (`-h`, `--help`, `-?`). Invoking `ainetreview` without arguments or with `--help` displays usage instructions and the command on standard output and exits with code `0`. `ainetreview review --help` displays detailed help.

The optional `project-path` argument defaults to the current working directory and may be relative or absolute. It must specify a directory (the project or solution root), not a file. The command uses `ainetreview.json` directly under the selected project root. Calls with an unknown subcommand or unsupported options return `INVALID_INPUT` with exit code `2`. The separate manual `audit-targets` workflow uses its selected host-side profile; see [Build and Tests](../development/build-and-tests.md).

When `ainetreview.json` does not exist, the command searches only the project root's top level for `.slnx` and `.sln` files (extensions are matched case-insensitively), generates the configuration from the registered analyses' defaults, and continues with that file. It writes to a unique temporary file in the project root and moves that file into place only after the write completes; cancellation or a write failure therefore leaves no partial final configuration, and a competing configuration is never replaced. Solution selection prefers a filename stem matching the project directory name (case-insensitively), then `.slnx`, then ordinal filename order. For example, in a directory named `Project`, `Project.sln` wins over `Other.slnx`; without a name match, an `.slnx` wins over any `.sln`, and ties are resolved by ordinal filename order. If no solution is found, the command returns `INVALID_INPUT` with exit code `2` and does not create a configuration file.

The `review` command validates the configuration, loads and checks the configured solution, runs its configured analyses, and publishes the complete Markdown report set. The final directory rename retries Windows access-denied, sharing-violation, and lock-violation errors with a bounded delay to tolerate short-lived file locks; cancellation remains effective during these retries. On publication failure, the writer makes the same bounded best-effort retries while removing its own temporary directory. If an external handle continues to prevent deletion after that window, the `.ainetreview-tmp-<runId>` directory can remain for manual cleanup after the handle closes; the writer does not sweep other temporary directories or published runs.

On review success, stdout contains exactly one compact JSON line, for example:

```json
{"status":"completed","runId":"20260928T163802Z-a1b2c3d4","indexPath":"audit-reporting/20260928T163802Z-a1b2c3d4/index.md","counts":{"detected":0}}
```

`indexPath` is relative to the configuration's project root and names the run-root `index.md`. The root index routes to the complete `production/`, `tests/`, and `mixed/` area indexes and `maps/index.md`. The maps index routes to project maps and `maps/audit/index.md`; the audit map groups findings by project and representative source file, then lists each canonical analysis-report path once with all routed stable IDs. Analysis details retain decisive evidence and project-root-relative source locations; related findings group their exact IDs by report-relative path. Generated reports use plain paths without Markdown links or HTML anchors. The complete report set is published through the existing atomic directory rename; failure or cancellation publishes no partial report. The response keeps its existing JSON shape. A complete run with findings also exits with code `0`. Errors produce one `{"code":"...","message":"..."}` JSON line on stderr and no success response on stdout. Exit codes are `2` for `INVALID_INPUT`, `3` for `ANALYSIS_FAILED`, `4` for `REPORT_FAILED` or `LOGGING_FAILED`, and `130` for `CANCELLED`. Parser errors use `INVALID_INPUT`; progress and log events are never written to either process stream.



Serilog initializes before parsing. Its only sink writes to `<AppContext.BaseDirectory>/logs/`, independent of the current directory, configuration path, and project root. The daily file rotates at 10 MiB, keeps at most 30 files, and supports concurrent processes. Log events include command, run ID when available, and failure context; configuration contents and source text are not logged. If logging cannot start, the process returns `LOGGING_FAILED` with code `4`.

The adapter accepts injected services and input/output streams. IntegrationTests use this same adapter with a test-only fixture analysis; the executable composition root registers the production analyses `code-size-candidates`, `method-control-flow-outliers`, `dead-code-candidates`, `duplicate-code-candidates`, `indirection-drift-candidates`, `missing-test-evidence-candidates`, `non-ascii-identifiers`, `structural-duplication-candidates`, `type-dependency-cycle-candidates`, and `type-dependency-hub-candidates`. The structural analysis is enabled by default, has no custom options, and publishes exact local/parameter-normalized statement fragments with every occurrence location when findings exist. Existing configurations that omit its key do not enable it implicitly.
