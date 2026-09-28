# Build and Tests

## Prerequisites

- .NET 10 SDK (pinned via `global.json`, version `10.0.203` or compatible feature release)

## Solution Structure

The solution `AiNetReview.slnx` contains five projects:

- `src/AiNetReview.Core/`: Core library for configuration, analysis, rules, and current finding results.
- `src/AiNetReview/`: Host executable with DI composition, the `review --config` command, and logging.
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

## Host Logging

The host initializes Serilog before command parsing. Its only sink writes `ainetreview-<date>.log` under the host executable's `logs/` directory, independent of the working directory. Files roll daily and at 10 MiB, retain at most 30 files, and allow concurrent host processes to write. If the directory or active log file cannot be opened for writing, the host exits with code `4` and writes a `LOGGING_FAILED` JSON error to stderr without writing to stdout. Process-level coverage is in `HostProcessIntegrationTests`.

The host integration tests also run the production executable against generated projects and check JSON stream shape, exit codes, publication on a complete empty run, no publication after analysis failure, executable-relative logs, size rotation, and concurrent processes. `HostAdapterIntegrationTests` runs the same CLI adapter with an explicitly registered fixture rule and repeats scans after changing source and options, checking current findings and preservation of earlier reports.

`.editorconfig` enables build errors for selected resource, async/task, threading, API-result, and regex-timeout defects. It explicitly disables design, complexity-related, context-dependent performance, and cancellation-forwarding diagnostics as build gates. The selected technical diagnostics apply to test projects and TestKit; suppress a verified false positive at its specific location.

## Running Tests

Run the test suites using the dedicated test scripts:

```powershell
# FastTests (dumps full console log to temp/test-fast.log and TRX to TestResults/FastTests.trx)
pwsh -File ./scripts/test-fast.ps1

# IntegrationTests (dumps full console log to temp/test-integration.log and TRX to TestResults/IntegrationTests.trx)
pwsh -File ./scripts/test-integration.ps1

# Separate release-only infrastructure load gate (Windows, >=4 logical CPUs, >=16 GiB RAM)
pwsh -File ./scripts/test-performance.ps1
```

The normal IntegrationTests script excludes the test tagged `Category=Performance`; the dedicated performance script runs that deterministic 180,000-line test against the production executable. It records elapsed time, peak private bytes, source document count, project count, and machine capacity in the test output. Release acceptance requires at most 10 minutes and 6 GiB peak private bytes.

Agents and automation tools can inspect the static log files in `temp/` directly.
