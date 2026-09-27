# Current-state documentation

The host initializes executable-relative Serilog file logging. The core exposes rule registration, descriptor-based catalog generation, strict configuration validation, project path boundaries, and Git-independent `.sln`/`.slnx` loading with compilation checks. CLI and MCP review commands, finding reconciliation, storage, and reports are not implemented yet.

| Area | Pages |
| --- | --- |
| Architecture | [Overview](architecture/overview.md), [Dependencies](architecture/dependencies.md) |
| Configuration | [File format](configuration/file-format.md), [Rule catalog](configuration/rule-catalog.md) |
| Interfaces | [CLI](interfaces/cli.md), [MCP](interfaces/mcp.md) |
| Review | [Findings](review/findings.md), [Decisions](review/decisions.md), [Storage](review/storage.md), [Reports](review/reports.md) |
| Development | [Build and tests](development/build-and-tests.md), [Adding rules](development/adding-rules.md) |

Specifications remain under [`tasks/initial-infrastructure/`](../tasks/initial-infrastructure/README.md) until implemented.
