# Current-state documentation

AiNetReview is currently in the initial skeleton stage. The project structure and test execution are verified. The host initializes executable-relative Serilog file logging, and the core exposes rule registration, descriptor validation, and descriptor-based catalog generation. Most other linked pages remain placeholders until their behavior is implemented.

| Area | Pages |
| --- | --- |
| Architecture | [Overview](architecture/overview.md), [Dependencies](architecture/dependencies.md) |
| Configuration | [File format](configuration/file-format.md), [Rule catalog](configuration/rule-catalog.md) |
| Interfaces | [CLI](interfaces/cli.md), [MCP](interfaces/mcp.md) |
| Review | [Findings](review/findings.md), [Decisions](review/decisions.md), [Storage](review/storage.md), [Reports](review/reports.md) |
| Development | [Build and tests](development/build-and-tests.md), [Adding rules](development/adding-rules.md) |

Specifications remain under [`tasks/initial-infrastructure/`](../tasks/initial-infrastructure/README.md) until implemented.
