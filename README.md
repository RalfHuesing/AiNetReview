# AiNetReview

AiNetReview helps audit C# code after the usual build, tests, and static analysis. It loads a solution and produces focused review signals in Markdown. Each finding points to source locations and gives a reviewer a concrete place to investigate. A human or review agent decides whether a change is warranted; AiNetReview does not refactor code or fail a build because of a review finding.

## Current review signals

| Review analysis | What to investigate |
| --- | --- |
| `method-control-flow-outliers` | Methods with unusually many decisions or deeply nested decision paths within their project. |
| `dead-code-candidates` | Types and methods without known uses in the analyzed solution; indirect or external uses may still exist. |
| `duplicate-code-candidates` | Groups of substantially similar method bodies; similarity does not by itself justify merging them. |

The reports are prompts for an audit, not defect claims. See [Current findings](docs/review/findings.md) for the analyses' scope and limitations.

## Run a review

GitHub [releases](https://github.com/RalfHuesing/AiNetReview/releases) provide Windows x64 archives when a version is published. To run from source, install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and build `AiNetReview.slnx`.

1. Place `ainetreview.json` in the root of the project you want to review. Start from the [repository example](ainetreview.json), then set `solution` to a `.sln` or `.slnx` path relative to that root. Adjust the enabled analyses and `outputDirectory` as needed.
2. Build and test the target solution first. From an extracted release archive, run AiNetReview with an absolute path to its configuration:

   ```powershell
   .\AiNetReview.exe review --config C:\path\to\project\ainetreview.json
   ```

3. Open the `index.md` named by the command's JSON response. The `indexPath` value is relative to the target project root. Each run gets its own report directory, so earlier reports remain available.

The command requires a loadable C# solution without compiler errors. A completed review exits with code `0` even when it reports findings. See the [configuration reference](docs/configuration/file-format.md) and [CLI contract](docs/interfaces/cli.md) for options and failure codes.

## Development and releases

See [Build and tests](docs/development/build-and-tests.md) for local build, test, and release commands. Version tags matching `v*` trigger the GitHub Actions workflow that publishes a Windows x64 archive.

The [current-state documentation](docs/README.md) describes implemented behavior. Specifications and ideas under [`tasks/`](tasks/) may describe future work.

## License

AiNetReview is available under the [MIT License](LICENSE).
