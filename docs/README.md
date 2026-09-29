# Current-state documentation

The host provides the `review [project-path]` and `baseline [project-path]` commands. Both use `ainetreview.json` in the selected project root and create it from the discovered solution when it is missing. The host initializes executable-relative Serilog file logging and composes configuration validation, solution loading, analysis execution, baseline handling, and Markdown publication. Its production registry contains `method-control-flow-outliers`, which reports methods that stand out in decision count or decision nesting within their project; `dead-code-candidates`, which reports possible unreferenced declarations; `duplicate-code-candidates`, which reports clusters of substantially similar method bodies across production C# projects; `indirection-drift-candidates`, which reports qualifying current paths of statically bound transparent forwarding across production C# files; and `non-ascii-identifiers`, which reports declarations containing non-ASCII characters in production C# code. The repository-root `ainetreview.json` lists every production analysis; individual projects can disable a listed analysis with `enabled: false`. Each review atomically publishes a root index plus `changed-files/` and `all-findings/` report views, with analysis files only where that view has findings. Core also provides project path boundaries, Git-independent `.sln`/`.slnx` loading with materialized source text and compilation checks, a stateless analysis runner, and a Markdown report writer that preserves older runs.

| Area | Pages |
| --- | --- |
| Architecture | [Overview](architecture/overview.md), [Dependencies](architecture/dependencies.md) |
| Configuration | [File format](configuration/file-format.md) |
| Interfaces | [CLI](interfaces/cli.md) |
| Review | [Findings](review/findings.md) |
| Development | [Build and tests](development/build-and-tests.md), [Adding review analyses](development/adding-review-analyses.md) |

Planned specifications remain under [`tasks/`](../tasks/) until implemented.
