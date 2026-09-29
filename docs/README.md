# Current-state documentation

The host provides the synchronous `review --config` command, initializes executable-relative Serilog file logging, and composes configuration validation, solution loading, analysis execution, and Markdown publication. Its production registry contains `method-control-flow-outliers`, which reports methods that stand out in decision count or decision nesting within their project; `dead-code-candidates`, which reports possible unreferenced declarations; and `duplicate-code-candidates`, which reports clusters of substantially similar method bodies across production C# projects. The repository-root `ainetreview.json` lists every production analysis; individual projects can disable a listed analysis with `enabled: false`. Each audit atomically publishes a root index plus `changed-files/` and `all-findings/` report views, with analysis files only where that view has findings. Core also provides project path boundaries, Git-independent `.sln`/`.slnx` loading with materialized source text and compilation checks, a stateless analysis runner, and a Markdown report writer that preserves older runs.

| Area | Pages |
| --- | --- |
| Architecture | [Overview](architecture/overview.md), [Dependencies](architecture/dependencies.md) |
| Configuration | [File format](configuration/file-format.md) |
| Interfaces | [CLI](interfaces/cli.md) |
| Review | [Findings](review/findings.md) |
| Development | [Build and tests](development/build-and-tests.md), [Adding review analyses](development/adding-review-analyses.md) |

Planned specifications remain under [`tasks/`](../tasks/) until implemented.
