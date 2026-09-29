# Dependencies

The executable host references Core and uses `Microsoft.Extensions.DependencyInjection`, `System.CommandLine`, and Serilog packages. Core references Roslyn/MSBuild packages for loading and compiling solutions. Package versions are centrally pinned in `Directory.Packages.props`.

TestKit has no Core dependency. FastTests reference Core and TestKit. IntegrationTests reference Core, the host, and TestKit. The host does not load test assemblies or fixture analyses.

There is no MCP package, finding store, generated analysis catalog, or persistence dependency in the current solution.
