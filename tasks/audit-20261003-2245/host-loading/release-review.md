# Release verification review

## Observed execution paths

- `.github/workflows/release.yml:3-6` starts on any pushed tag matching `v*`; it does not check a branch, run tests, run the repository's release script, or require a passing test job. Its job installs .NET 10, runs `dotnet publish` Release / win-x64 / self-contained, packages the output, then creates a GitHub Release (`:24-62`). No runtime smoke runs against the published archive.
- The documented release path is `scripts/release.ps1` (`docs/development/build-and-tests.md:69-83`). That script runs `dotnet build` and the FastTests and IntegrationTests suites before it changes the project version or commits/pushes the version commit and tag (`scripts/release.ps1:138-172, 284-305`). Performance tests are explicitly opt-in through `-IncludePerformanceTest` (`:161-167`), and docs call the performance gate separate/release-only (`build-and-tests.md:61-65`).
- Those `dotnet test` invocations do not specify `-c Release`, so test defaults to Debug. The process-level repository integration test also explicitly resolves and launches `src/AiNetReview/bin/Debug/net10.0` (`tests/AiNetReview.IntegrationTests/HostProcessIntegrationTests.cs:102-133`); docs make this explicit (`build-and-tests.md:43`). The published artifact is Release `win-x64`, `--self-contained true`, and is not smoke-tested by this path.
- The current root project files have no observed configuration-conditional behavior in `AiNetReview.csproj` or `Directory.Build.props` (searched `src`, `tests`, and central props/targets for `Configuration`, `DefineConstants`, optimization, runtime identifier and publish conditions). This reduces evidence for a presently divergent code path but does not exercise the actual packaged layout/runtime.

## Skeptical assessment

This is a real release-control gap, but its priority depends on treating `release.ps1` as mandatory policy. Repository docs present it as the automated release process and it gates releases when followed. The GitHub workflow is independently triggerable by any `v*` tag, however, so that policy is bypassable: a direct tag push releases after compilation only. Even the normal script does not test the Release win-x64 self-contained output; it tests Debug and the workflow later publishes Release without tests. A Release compile catches many source/package problems, but not runtime and archive-layout issues.

The strongest benign explanation is that releases are intentionally limited by convention to `release.ps1`, Debug/Release share sources and dependencies, and the tag workflow's responsibility is only packaging after local test gates. No branch protection, required checks, protection rule on tags, or release-policy mandate was inspected, so that explanation remains possible rather than proven. No Release runtime breakage was demonstrated.

Assessment: plausible **P2 release-verification risk, medium-low confidence**, not a confirmed product runtime defect. If the audit avoids policy-only expectations and accepts the documented local release-script gate, this should be recorded as a bounded uncertainty rather than a finding. To close it, either make the workflow itself enforce test gates and smoke the published artifact, or document/enforce protected tags and the release script as the only authorized path. Preserve Windows x64 self-contained release contents and current local version/tag workflow.

## Commands and limits

Read-only inspection only. No release script, tag, push, archive generation, or publish was executed. The root agent separately reports successful Debug and verification-copy builds; this pass did not run tests. Source lines and script paths are recorded above.
