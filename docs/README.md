# Current-state documentation

The host initializes executable-relative Serilog file logging. The core exposes rule registration, descriptor-based catalog generation, strict configuration validation, project path boundaries, Git-independent `.sln`/`.slnx` loading with compilation checks, and a generic finding runner. Finding state currently lives only in a process-local transitional store; durable storage, verdicts, reports, and CLI/MCP review commands are not implemented yet.

| Area | Pages |
| --- | --- |
| Architecture | [Overview](architecture/overview.md), [Dependencies](architecture/dependencies.md) |
| Configuration | [File format](configuration/file-format.md), [Rule catalog](configuration/rule-catalog.md) |
| Interfaces | [CLI](interfaces/cli.md), [MCP](interfaces/mcp.md) |
| Review | [Findings](review/findings.md), [Decisions](review/decisions.md), [Storage](review/storage.md), [Reports](review/reports.md) |
| Development | [Build and tests](development/build-and-tests.md), [Adding rules](development/adding-rules.md) |

Specifications remain under [`tasks/initial-infrastructure/`](../tasks/initial-infrastructure/README.md) until implemented.
