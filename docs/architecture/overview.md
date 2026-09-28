# Architecture

The solution has five projects: Core, the executable host, TestKit, FastTests, and IntegrationTests. Core owns configuration validation, MSBuild/Roslyn solution loading, shared production/test and generated-source classification, checked project-root-relative path conversion, rule descriptors and registration, the rule runner, and finding models. The host composes Core services and initializes logging. TestKit is independent of Core; FastTests reference Core and TestKit; IntegrationTests reference Core, host, and TestKit.

The Core does not reference the host. The host is the composition root. Product rules are registered explicitly; the production registry contains `method-control-flow-outliers`. The current dependency direction and package references are listed in [Dependencies](dependencies.md).

Core opens `.sln` and `.slnx` files through `MSBuildWorkspace`, checks C# source paths against the project and output directories, and materializes every C# document's text into the returned immutable Roslyn `Solution`. It creates and checks each C# project's compilation before handing the solution to the analysis runner. A solution without a C# project, an incomplete load, or a compilation error fails the analysis.

The host exposes the single `review --config` command. Its CLI adapter is injectable for streams and service providers; the production composition root registers `method-control-flow-outliers`. The adapter connects the configuration validator, solution loader, review runner, and Markdown report writer, returning one JSON process response only after publication succeeds. The host initializes executable-relative Serilog before parsing. See [CLI](../interfaces/cli.md) for command and logging contracts.
