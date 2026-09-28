# Current-state documentation

The host provides the synchronous `review --config` command, initializes executable-relative Serilog file logging, and composes configuration validation, solution loading, rule execution, and Markdown publication. Its production registry contains `method-control-flow-outliers`, which reports methods that stand out in decision count or decision nesting within their project; `dead-code-candidates`, which reports possible unreferenced declarations; and `duplicate-code-candidates`, which reports clusters of substantially similar method bodies across production C# projects. The repository-root `ainetreview.json` lists every production rule; individual projects can disable a listed rule with `enabled: false`. Audits publish an index and Markdown files only for rules with current findings. Core also provides project path boundaries, Git-independent `.sln`/`.slnx` loading with materialized source text and compilation checks, a stateless rule runner, and a Markdown report writer that publishes each completed report set atomically while preserving older runs.

| Area | Pages |
| --- | --- |
| Architecture | [Overview](architecture/overview.md), [Dependencies](architecture/dependencies.md) |
| Configuration | [File format](configuration/file-format.md) |
| Interfaces | [CLI](interfaces/cli.md) |
| Review | [Findings](review/findings.md) |
| Development | [Build and tests](development/build-and-tests.md), [Adding rules](development/adding-rules.md) |

Planned specifications remain under [`tasks/`](../tasks/) until implemented.
