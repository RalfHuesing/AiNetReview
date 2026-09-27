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

Core, Host, and TestKit expose their internal members to both test assemblies through `InternalsVisibleTo`. This supports tests of internal components as the implementation grows.

## Building

Build the solution using the PowerShell build script, which logs the full console output to `temp/build.log`:

```powershell
pwsh -File ./scripts/build.ps1
```

TreatWarningsAsErrors and Nullable reference types are enabled across all projects in `Directory.Build.props`.

`.editorconfig` enables build errors for selected resource, async/task, threading, API-result, and regex-timeout defects. It explicitly disables design, complexity-related, context-dependent performance, and cancellation-forwarding diagnostics as build gates. The selected technical diagnostics apply to test projects and TestKit; suppress a verified false positive at its specific location.

## Running Tests

Run the test suites using the dedicated test scripts:

```powershell
# FastTests (dumps full console log to temp/test-fast.log and TRX to TestResults/FastTests.trx)
pwsh -File ./scripts/test-fast.ps1

# IntegrationTests (dumps full console log to temp/test-integration.log and TRX to TestResults/IntegrationTests.trx)
pwsh -File ./scripts/test-integration.ps1
```

Agents and automation tools can inspect the static log files in `temp/` directly.
