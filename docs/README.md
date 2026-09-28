# Current-state documentation

The host provides the synchronous `review --config` command, initializes executable-relative Serilog file logging, and composes configuration validation, solution loading, rule execution, and Markdown publication. The production rule registry contains only `template-noop`. Core also provides project path boundaries, Git-independent `.sln`/`.slnx` loading with materialized source text and compilation checks, a stateless rule runner, and a Markdown report writer that publishes each completed report set atomically while preserving older runs.

| Area | Pages |
| --- | --- |
| Architecture | [Overview](architecture/overview.md), [Dependencies](architecture/dependencies.md) |
| Configuration | [File format](configuration/file-format.md) |
| Interfaces | [CLI](interfaces/cli.md) |
| Review | [Findings](review/findings.md) |
| Development | [Build and tests](development/build-and-tests.md), [Adding rules](development/adding-rules.md) |

Specifications remain under [`tasks/initial-infrastructure/`](../tasks/initial-infrastructure/README.md) until implemented.
