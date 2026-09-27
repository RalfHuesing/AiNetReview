# Build and Tests

## Prerequisites

- .NET 10 SDK (pinned via `global.json`, version `10.0.203` or compatible feature release)

## Solution Structure

The solution `AiNetReview.slnx` contains five projects:

- `src/AiNetReview.Core/`: Core library for configuration, analysis, rules, findings, storage, reporting, and catalog.
- `src/AiNetReview/`: Host executable for CLI and MCP entry points.
- `tests/AiNetReview.TestKit/`: Shared test support infrastructure (e.g. isolated temp directory lifecycle and test helpers).
- `tests/AiNetReview.FastTests/`: Unit and component test suite.
- `tests/AiNetReview.IntegrationTests/`: End-to-end and host integration test suite.

## Building

Build the entire solution:

```bash
dotnet build
```

TreatWarningsAsErrors and Nullable reference types are enabled across all projects in `Directory.Build.props`.

## Running Tests

Execute both test projects:

```bash
dotnet test
```
