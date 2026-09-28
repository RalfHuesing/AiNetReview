# Current-state documentation

The host initializes executable-relative Serilog file logging, but command handling is still a placeholder. Core currently provides explicit rule registration, strict configuration validation, project path boundaries, Git-independent `.sln`/`.slnx` loading with compilation checks, and a stateless rule runner. Reports and the functional CLI are not implemented yet.

| Area | Pages |
| --- | --- |
| Architecture | [Overview](architecture/overview.md), [Dependencies](architecture/dependencies.md) |
| Configuration | [File format](configuration/file-format.md) |
| Interfaces | [CLI](interfaces/cli.md) |
| Review | [Findings](review/findings.md) |
| Development | [Build and tests](development/build-and-tests.md), [Adding rules](development/adding-rules.md) |

Specifications remain under [`tasks/initial-infrastructure/`](../tasks/initial-infrastructure/README.md) until implemented.
