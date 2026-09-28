# Architecture

The solution has five projects: Core, the executable host, TestKit, FastTests, and IntegrationTests. Core owns configuration validation, MSBuild/Roslyn solution loading, rule descriptors and registration, the rule runner, and finding models. The host composes Core services and initializes logging. TestKit is independent of Core; FastTests reference Core and TestKit; IntegrationTests reference Core, host, and TestKit.

The Core does not reference the host. The host is the composition root. Product rules are registered explicitly; only `template-noop` is currently registered in production. The current dependency direction and package references are listed in [Dependencies](dependencies.md).

Core opens `.sln` and `.slnx` files through `MSBuildWorkspace`, checks C# source paths against the project and output directories, and materializes every C# document's text into the returned immutable Roslyn `Solution`. It creates and checks each C# project's compilation before handing the solution to the analysis runner. A solution without a C# project, an incomplete load, or a compilation error fails the analysis.

The host command handler and Markdown report publication are not implemented yet. Their approved contracts are in the [initial infrastructure specification](../../tasks/initial-infrastructure/README.md).
