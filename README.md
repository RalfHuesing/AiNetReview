# AiNetReview

AiNetReview is a local command-line tool that loads a C# solution, runs its configured review rules against one immutable source view, and publishes Markdown reports. Its first production review rule reports methods that stand out in control-flow decisions or nesting as candidates for human review.

- [Current-state documentation](docs/README.md)
- [Agent instructions](AGENTS.md)

See [Build and Tests](docs/development/build-and-tests.md) for build, test, and release performance commands.

The `tasks/` directory contains plans and specifications. The `docs/` directory is reserved for verified, implemented behavior.
