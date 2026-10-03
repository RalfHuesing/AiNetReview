# AiNetReview

AiNetReview helps audit C# code after the usual build, tests, and static analysis. It loads a solution and produces focused review signals in Markdown. Each finding points to source locations and gives a reviewer a concrete place to investigate. A human or review agent decides whether a change is warranted; AiNetReview does not refactor code or fail a build because of a review finding.

## Current review signals

| Review analysis | What to investigate |
| --- | --- |
| `method-control-flow-outliers` | Methods with unusually many decisions or deeply nested decision paths within their project. |
| `dead-code-candidates` | Types and methods without known uses in the analyzed solution; indirect or external uses may still exist. |
| `duplicate-code-candidates` | Groups of substantially similar method bodies across production and test projects; similarity does not by itself justify merging them. |
| `structural-duplication-candidates` | Exact repeated statement fragments across production and test projects after normalizing bound local and parameter names; retained syntax distinctions still matter. |
| `indirection-drift-candidates` | Current statically declared paths with at least two transparent forwarding edges across three types and three source files in one production or test project; runtime dispatch and historical growth are not measured. |
| `missing-test-evidence-candidates` | Structurally nontrivial production functions with no static test path, or only an indirect path when both higher complexity thresholds are met; this is not runtime coverage evidence. |
| `non-ascii-identifiers` | Production and test declarations whose identifiers contain characters outside the supported ASCII set. |
| `code-size-candidates` | Executable members, classes, and source files selected by project-relative size and control-flow thresholds for focused review. |
| `type-dependency-cycle-candidates` | Maximal groups of at least three production types across at least three declaration files with mutual direct static dependencies; reports every internal edge witness and one example cycle. |
| `type-dependency-hub-candidates` | Production types with at least 10 distinct direct production consumers and at least 10 distinct direct production dependencies; both thresholds are independently configurable, and test consumers are listed separately. |

Each run is a complete audit of the loaded solution by every enabled configured analysis. Reports disclose disabled analyses, project roles, broad exclusions, and analysis-specific uncertainty; static analysis cannot establish behavior outside the documented scope. Reports are prompts for investigation, not defect claims. See [Current findings](docs/review/findings.md) for the analyses' scope and limitations.

## Run a review

GitHub [releases](https://github.com/RalfHuesing/AiNetReview/releases) provide Windows x64 archives when a version is published. To run from source, install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and build `AiNetReview.slnx`.

1. Place `ainetreview.json` in the root of the project you want to review. Start from the [repository example](ainetreview.json), then set `solution` to a `.sln` or `.slnx` path relative to that root. Adjust the enabled analyses and `outputDirectory` as needed.
2. Build and test the target solution first. From an extracted release archive, pass the project root to the review command:

   ```powershell
   .\AiNetReview.exe review C:\path\to\project
   ```

3. Open the `index.md` named by the command's JSON response. It provides shared audit guidance and routes to `production/`, `tests/`, `mixed/`, and `maps/index.md`. Choose project structure and type dependency maps there; `maps/audit/index.md` routes findings to canonical analysis details. Each finding area has an `index.md` and direct `<area>/<analysis>.md` reports for analyses with findings. Detailed reports carry concise evidence and project-root-relative source locations. Each run gets its own report directory, so earlier reports remain available. See [Current findings](docs/review/findings.md) for the map contract.

The command requires a loadable C# solution without compiler errors. A completed review exits with code `0` even when it reports findings. See the [configuration reference](docs/configuration/file-format.md) and [CLI contract](docs/interfaces/cli.md) for options and failure codes.

The command creates a default `ainetreview.json` in the project root when it is missing and a solution file is found there.

## Development and releases

See [Build and tests](docs/development/build-and-tests.md) for local build, test, and release commands. Version tags matching `v*` trigger the GitHub Actions workflow that publishes a Windows x64 archive.

The [current-state documentation](docs/README.md) describes implemented behavior. Specifications and ideas under [`tasks/`](tasks/) may describe future work.

## License

AiNetReview is available under the [MIT License](LICENSE).
