# AiNetReview

AiNetReview is a local command-line tool that loads a C# solution, runs its configured review rules against one immutable source view, and publishes Markdown reports. The current production rule is `template-noop`; the finding rule is available only in IntegrationTests.

- [Initial infrastructure specification](tasks/initial-infrastructure/README.md)
- [Current-state documentation](docs/README.md)
- [Agent instructions](AGENTS.md)

See [Build and Tests](docs/development/build-and-tests.md) for build, test, and release performance commands.

The `tasks/` directory contains plans and specifications. The `docs/` directory is reserved for verified, implemented behavior.
