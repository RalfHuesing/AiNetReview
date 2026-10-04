namespace AiNetReview.FastTests.Reporting;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Reporting;

public sealed class MapsReportWriterTests
{
    [Fact]
    public async Task WriteMapsAsync_PublishesCompactHubAndSeparateCompleteEdgeMaps()
    {
        using var temp = TestTempDirectory.Create();
        var maps = CreateMaps();

        await MapsReportWriter.WriteMapsAsync(temp.DirectoryPath, maps, CancellationToken.None);

        var production = await ReadDependenciesAsync(temp.DirectoryPath, "production", "p-production");
        var productionOutgoing = await ReadDependencyPartAsync(temp.DirectoryPath, "production", "p-production", "dependencies-outgoing.md");
        var productionIncoming = await ReadDependencyPartAsync(temp.DirectoryPath, "production", "p-production", "dependencies-incoming.md");
        Assert.Contains("# Type dependencies — `src/Domain/Domain.csproj`", production, StringComparison.Ordinal);
        Assert.Contains("Outgoing: 5; incoming: 8.", production, StringComparison.Ordinal);
        Assert.Contains("`dependencies-outgoing.md`", production, StringComparison.Ordinal);
        Assert.Contains("`dependencies-incoming.md`", production, StringComparison.Ordinal);
        Assert.Contains("## Project routes", production, StringComparison.Ordinal);
        Assert.Contains("| Project path | Role | Structure route | Dependency route | Outgoing route |", production, StringComparison.Ordinal);
        Assert.Contains("| `tests/Domain.Tests/Domain.Tests.csproj` | tests | `../../tests/p-tests-one/structure.md` | `../../tests/p-tests-one/dependencies.md` | `../../tests/p-tests-one/dependencies-outgoing.md` |", production, StringComparison.Ordinal);
        Assert.Contains("| `src/Shared/Shared.csproj` | production | `../../production/p-shared/structure.md` | `../../production/p-shared/dependencies.md` | `../../production/p-shared/dependencies-outgoing.md` |", production, StringComparison.Ordinal);
        Assert.DoesNotContain("Runner` →", production, StringComparison.Ordinal);
        Assert.DoesNotContain("Shared.cs:12", production, StringComparison.Ordinal);

        Assert.Contains("# Outgoing type dependencies — `src/Domain/Domain.csproj`", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("`dependencies.md`", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("`structure.md`", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("project-root-relative", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("Lnn", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("`global::App.Domain.Widget`", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("`global::App.Domain.Cache`", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("`global::Infrastructure.Cache`", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("Outer<T>.Inner<U>", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("global::Shared.Token (ProjectPath: src/Shared/Shared.csproj)", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("src/Domain/Runner.Part1.cs", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("src/Domain/Runner.Part2.cs", productionOutgoing, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(productionOutgoing, "### `src/Domain/Runner.Part1.cs`"));
        Assert.Equal(1, CountOccurrences(productionOutgoing, "### `src/Domain/Runner.Part2.cs`"));
        Assert.Contains("- `Runner` → `global::App.Domain.Widget`: L10 ExplicitTypeUse, L10 MemberUse, L11 MemberUse", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("- `Runner` → `global::App.Domain.Cache`: L15 Inheritance", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("- `Runner` → `global::Infrastructure.Cache`: L30 MemberUse, L31 MemberUse", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("- `Runner` → `Outer<T>.Inner<U>`: L42 Inheritance", productionOutgoing, StringComparison.Ordinal);
        Assert.Contains("- `Runner` → `global::Shared.Token (ProjectPath: src/Shared/Shared.csproj)`: L44 MemberUse", productionOutgoing, StringComparison.Ordinal);
        Assert.DoesNotContain("OtherConsumer", productionOutgoing, StringComparison.Ordinal);
        Assert.DoesNotContain("WidgetTests", productionOutgoing, StringComparison.Ordinal);

        Assert.Contains("# Incoming type dependencies — `src/Domain/Domain.csproj`", productionIncoming, StringComparison.Ordinal);
        Assert.Contains("`dependencies.md`", productionIncoming, StringComparison.Ordinal);
        Assert.Contains("`structure.md`", productionIncoming, StringComparison.Ordinal);
        Assert.Contains("project-root-relative", productionIncoming, StringComparison.Ordinal);
        Assert.Contains("Lnn", productionIncoming, StringComparison.Ordinal);
        Assert.DoesNotContain("Runner` →", productionIncoming, StringComparison.Ordinal);
        var consumerDeclarations = ExtractSection(productionIncoming, "## Consumer declarations", "## Incoming edges");
        var incomingEdges = ExtractSection(productionIncoming, "## Incoming edges", null);
        var cacheIncoming = ExtractSection(incomingEdges, "### `global::App.Domain.Cache`", "### `global::App.Domain.Widget`");
        Assert.Contains("### `src/Shared/Shared.csproj`", consumerDeclarations, StringComparison.Ordinal);
        Assert.Contains("### `tests/Domain.Tests/Domain.Tests.csproj`", consumerDeclarations, StringComparison.Ordinal);
        Assert.Contains("### `tests/Other.Tests/Other.Tests.csproj`", consumerDeclarations, StringComparison.Ordinal);
        Assert.Contains("- `OtherConsumer`: `Shared.cs:12`", consumerDeclarations, StringComparison.Ordinal);
        Assert.Contains("- `WidgetTests`: `tests/Domain.Tests/WidgetTests.Part2.cs:20`, `tests/Domain.Tests/WidgetTests.cs:3`", consumerDeclarations, StringComparison.Ordinal);
        Assert.Contains("- `OtherWidgetTests`: `Shared.cs:3`", consumerDeclarations, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(consumerDeclarations, "tests/Domain.Tests/WidgetTests.cs:3"));
        Assert.Equal(1, CountOccurrences(consumerDeclarations, "tests/Domain.Tests/WidgetTests.Part2.cs:20"));
        Assert.Contains("### `global::App.Domain.Widget`", incomingEdges, StringComparison.Ordinal);
        Assert.Contains("- Production consumers (`src/Shared/Shared.csproj`): `OtherConsumer`", incomingEdges, StringComparison.Ordinal);
        Assert.Contains("- Test consumers (`tests/Other.Tests/Other.Tests.csproj`): `OtherWidgetTests`", incomingEdges, StringComparison.Ordinal);
        Assert.Contains("### `global::App.Domain.Cache`", incomingEdges, StringComparison.Ordinal);
        Assert.Contains("- Test consumers (`tests/Domain.Tests/Domain.Tests.csproj`): `WidgetTests`", cacheIncoming, StringComparison.Ordinal);
        Assert.DoesNotContain("#### Production consumers", productionIncoming, StringComparison.Ordinal);
        Assert.DoesNotContain("##### `src/Shared/Shared.csproj`", productionIncoming, StringComparison.Ordinal);
        Assert.DoesNotContain("None.", incomingEdges, StringComparison.Ordinal);

        foreach (var document in new[] { production, productionOutgoing, productionIncoming })
        {
            Assert.DoesNotContain("t-runner", document, StringComparison.Ordinal);
            Assert.DoesNotContain("t-widget", document, StringComparison.Ordinal);
            Assert.DoesNotContain("t-cache-domain", document, StringComparison.Ordinal);
            Assert.DoesNotContain("t-cache-infra", document, StringComparison.Ordinal);
            Assert.DoesNotContain("t-inner", document, StringComparison.Ordinal);
            Assert.DoesNotContain("t-token-local", document, StringComparison.Ordinal);
            Assert.DoesNotContain("t-test-one", document, StringComparison.Ordinal);
            Assert.DoesNotContain("t-test-two", document, StringComparison.Ordinal);
            Assert.DoesNotContain("<a ", document, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<table", document, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("{\"", document, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("Foreign type IDs", productionOutgoing, StringComparison.Ordinal);

        Assert.Equal(1, CountOccurrences(production, "`../../tests/p-tests-one/dependencies.md`"));
        Assert.Equal(1, CountOccurrences(production, "`../../tests/p-tests-two/dependencies.md`"));

        var tests = await ReadDependenciesAsync(temp.DirectoryPath, "tests", "p-tests-one");
        var testsOutgoing = await ReadDependencyPartAsync(temp.DirectoryPath, "tests", "p-tests-one", "dependencies-outgoing.md");
        var testsIncoming = await ReadDependencyPartAsync(temp.DirectoryPath, "tests", "p-tests-one", "dependencies-incoming.md");
        Assert.Contains("Outgoing: 2; incoming: 0.", tests, StringComparison.Ordinal);
        Assert.Contains("WidgetTests", testsOutgoing, StringComparison.Ordinal);
        Assert.Contains("- `WidgetTests` → `Widget`: L7 ExplicitTypeUse", testsOutgoing, StringComparison.Ordinal);
        Assert.Contains("No in-scope incoming direct type edges", testsIncoming, StringComparison.Ordinal);
        Assert.DoesNotContain("t-test-one", tests, StringComparison.Ordinal);
        Assert.DoesNotContain("t-widget", testsOutgoing, StringComparison.Ordinal);
        Assert.DoesNotContain("t-widget", testsIncoming, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteMapsAsync_UsesShortNamesOnlyWhenUniqueAcrossLocalAndForeignTypes()
    {
        using var temp = TestTempDirectory.Create();
        await MapsReportWriter.WriteMapsAsync(temp.DirectoryPath, CreateMaps(), CancellationToken.None);

        var production = await ReadDependenciesAsync(temp.DirectoryPath, "production", "p-production");

        var outgoing = await ReadDependencyPartAsync(temp.DirectoryPath, "production", "p-production", "dependencies-outgoing.md");
        var incoming = await ReadDependencyPartAsync(temp.DirectoryPath, "production", "p-production", "dependencies-incoming.md");
        Assert.Contains("`global::App.Domain.Widget`", outgoing, StringComparison.Ordinal);
        Assert.Contains("`global::App.Domain.Cache`", outgoing, StringComparison.Ordinal);
        Assert.Contains("`global::Infrastructure.Cache`", outgoing, StringComparison.Ordinal);
        Assert.Contains("`global::App.Domain.Widget`", incoming, StringComparison.Ordinal);
        Assert.Contains("src/Domain/Domain.csproj", production, StringComparison.Ordinal);
        Assert.Contains("tests/Domain.Tests/Domain.Tests.csproj", production, StringComparison.Ordinal);
        foreach (var id in new[] { "t-runner", "t-widget", "t-cache-domain", "t-cache-infra", "t-inner", "t-token-local", "t-token-foreign", "t-unused-widget", "t-other-consumer", "t-test-one", "t-test-two" })
        {
            Assert.DoesNotContain(id, production + outgoing + incoming, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task WriteMapsAsync_IsDeterministicForPermutedMapInput()
    {
        using var first = TestTempDirectory.Create();
        using var second = TestTempDirectory.Create();
        var maps = CreateMaps();
        var permuted = maps with
        {
            Projects = maps.Projects.Reverse().ToArray(),
            Types = maps.Types.Reverse().Select(type => type with
            {
                Declarations = type.Declarations.Reverse().ToArray(),
            }).ToArray(),
            TypeEdges = maps.TypeEdges.Reverse().Select(edge => edge with
            {
                Witnesses = edge.Witnesses.Reverse().ToArray(),
            }).ToArray(),
        };

        await MapsReportWriter.WriteMapsAsync(first.DirectoryPath, maps, CancellationToken.None);
        await MapsReportWriter.WriteMapsAsync(second.DirectoryPath, permuted, CancellationToken.None);

        foreach (var (role, key) in new[] { ("production", "p-production"), ("production", "p-shared"), ("tests", "p-tests-one"), ("tests", "p-tests-two") })
        {
            foreach (var fileName in new[] { "dependencies.md", "dependencies-outgoing.md", "dependencies-incoming.md", "structure.md" })
            {
                Assert.Equal(
                    await ReadMapFileAsync(first.DirectoryPath, role, key, fileName),
                    await ReadMapFileAsync(second.DirectoryPath, role, key, fileName));
            }
        }
    }

    [Fact]
    public async Task WriteMapsAsync_ProducesExplicitEmptyAndNamedUnwitnessedEdges()
    {
        using var temp = TestTempDirectory.Create();
        var projects = new[]
        {
            Project("p-empty", "src/Empty/Empty.csproj", ProjectRole.Production),
            Project("p-connected", "src/Connected/Connected.csproj", ProjectRole.Production),
        };
        var types = new[]
        {
            Type("t-source", "p-connected", "Domain", "Source", "Class", "Domain.Source", "Source.cs", 1),
            Type("t-target", "p-connected", "Domain", "Target", "Class", "Domain.Target", "Target.cs", 2),
        };
        var maps = new ReviewMaps(projects, [], types,
            [Edge("t-source", "t-target", false)]);

        await MapsReportWriter.WriteMapsAsync(temp.DirectoryPath, maps, CancellationToken.None);

        var empty = await ReadDependenciesAsync(temp.DirectoryPath, "production", "p-empty");
        var connected = await ReadDependenciesAsync(temp.DirectoryPath, "production", "p-connected");
        var emptyOutgoing = await ReadDependencyPartAsync(temp.DirectoryPath, "production", "p-empty", "dependencies-outgoing.md");
        var emptyIncoming = await ReadDependencyPartAsync(temp.DirectoryPath, "production", "p-empty", "dependencies-incoming.md");
        var connectedOutgoing = await ReadDependencyPartAsync(temp.DirectoryPath, "production", "p-connected", "dependencies-outgoing.md");
        var connectedIncoming = await ReadDependencyPartAsync(temp.DirectoryPath, "production", "p-connected", "dependencies-incoming.md");
        Assert.Contains("No in-scope direct type edges", empty, StringComparison.Ordinal);
        Assert.Contains("No in-scope outgoing direct type edges", emptyOutgoing, StringComparison.Ordinal);
        Assert.Contains("No in-scope incoming direct type edges", emptyIncoming, StringComparison.Ordinal);
        Assert.Contains("`Source` → `Target`", connectedOutgoing, StringComparison.Ordinal);
        Assert.Contains("without retained witnesses", connectedOutgoing, StringComparison.Ordinal);
        Assert.Contains("### `Target`", connectedIncoming, StringComparison.Ordinal);
        Assert.Contains("- Production consumers (`src/Connected/Connected.csproj`): `Source`", connectedIncoming, StringComparison.Ordinal);
        Assert.DoesNotContain("Source` →", connectedIncoming, StringComparison.Ordinal);
        Assert.DoesNotContain("t-source", connected + connectedOutgoing + connectedIncoming, StringComparison.Ordinal);
        Assert.DoesNotContain("t-target", connected + connectedOutgoing + connectedIncoming, StringComparison.Ordinal);
    }

    private static ReviewMaps CreateMaps()
    {
        var projects = new[]
        {
            Project("p-production", "src/Domain/Domain.csproj", ProjectRole.Production),
            Project("p-shared", "src/Shared/Shared.csproj", ProjectRole.Production),
            Project("p-tests-one", "tests/Domain.Tests/Domain.Tests.csproj", ProjectRole.Tests),
            Project("p-tests-two", "tests/Other.Tests/Other.Tests.csproj", ProjectRole.Tests),
        };
        var types = new[]
        {
            Type("t-runner", "p-production", "App", "Runner", "Class", "global::App.Runner", "src/Domain/Runner.Part1.cs", 10,
                new ReviewMapTypeDeclaration("src/Domain/Runner.Part2.cs", 30)),
            Type("t-widget", "p-production", "App.Domain", "Widget", "Class", "global::App.Domain.Widget", "src/Domain/Widget.cs", 2),
            Type("t-cache-domain", "p-production", "App.Domain", "Cache", "Class", "global::App.Domain.Cache", "src/Domain/Cache.cs", 4),
            Type("t-cache-infra", "p-production", "Infrastructure", "Cache", "Class", "global::Infrastructure.Cache", "src/Domain/Infrastructure/Cache.cs", 5),
            Type("t-inner", "p-production", "App", "Outer<T>.Inner<U>", "Class", "global::App.Outer<T>.Inner<U>", "src/Domain/Outer.cs", 8),
            Type("t-unused-widget", "p-production", "Infrastructure", "Widget", "Class", "global::Infrastructure.Widget", "src/Domain/Unused.cs", 1),
            Type("t-token-local", "p-production", "Shared", "Token", "Class", "global::Shared.Token", "src/Domain/Token.cs", 2),
            Type("t-token-foreign", "p-shared", "Shared", "Token", "Class", "global::Shared.Token", "src/Shared/Token.cs", 2),
            Type("t-other-consumer", "p-shared", "Shared", "OtherConsumer", "Class", "global::Shared.OtherConsumer", "Shared.cs", 12),
            Type("t-test-one", "p-tests-one", "App.Tests", "WidgetTests", "Class", "global::App.Tests.WidgetTests", "tests/Domain.Tests/WidgetTests.cs", 3,
                new ReviewMapTypeDeclaration("tests/Domain.Tests/WidgetTests.Part2.cs", 20)),
            Type("t-test-two", "p-tests-two", "App.Tests", "OtherWidgetTests", "Class", "global::App.Tests.OtherWidgetTests", "Shared.cs", 3),
        };
        var edges = new[]
        {
            Edge("t-runner", "t-widget", false,
                Witness("p-production", "src/Domain/Runner.Part1.cs", 10, "MemberUse"),
                Witness("p-production", "src/Domain/Runner.Part1.cs", 10, "ExplicitTypeUse"),
                Witness("p-production", "src/Domain/Runner.Part1.cs", 11, "MemberUse")),
            Edge("t-runner", "t-cache-domain", false, Witness("p-production", "src/Domain/Runner.Part1.cs", 15, "Inheritance")),
            Edge("t-runner", "t-cache-infra", false, Witness("p-production", "src/Domain/Runner.Part2.cs", 30, "MemberUse"),
                Witness("p-production", "src/Domain/Runner.Part2.cs", 31, "MemberUse")),
            Edge("t-runner", "t-inner", false, Witness("p-production", "src/Domain/Runner.Part2.cs", 42, "Inheritance")),
            Edge("t-runner", "t-token-foreign", false, Witness("p-production", "src/Domain/Runner.Part2.cs", 44, "MemberUse")),
            Edge("t-other-consumer", "t-widget", false, Witness("p-shared", "Shared.cs", 14, "MemberUse")),
            Edge("t-test-one", "t-widget", true, Witness("p-tests-one", "tests/Domain.Tests/WidgetTests.cs", 7, "ExplicitTypeUse")),
            Edge("t-test-one", "t-cache-domain", true, Witness("p-tests-one", "tests/Domain.Tests/WidgetTests.Part2.cs", 22, "MemberUse")),
            Edge("t-test-two", "t-widget", true, Witness("p-tests-two", "tests/Other.Tests/OtherWidgetTests.cs", 9, "MemberUse")),
        };
        return new ReviewMaps(projects, [], types, edges);
    }

    private static ReviewMapProject Project(string key, string path, ProjectRole role) =>
        new(key, path, role, ProjectClassificationReason.NoTestMarker, []);

    private static ReviewMapType Type(
        string id,
        string projectKey,
        string @namespace,
        string name,
        string kind,
        string fullyQualifiedName,
        string path,
        int line,
        params ReviewMapTypeDeclaration[] additionalDeclarations) =>
        new(id, projectKey, @namespace, name, kind, fullyQualifiedName,
            [new ReviewMapTypeDeclaration(path, line), .. additionalDeclarations]);

    private static ReviewMapTypeEdge Edge(string from, string to, bool isTestContext, params ReviewMapTypeWitness[] witnesses) =>
        new(from, to, isTestContext, witnesses);

    private static ReviewMapTypeWitness Witness(string projectKey, string path, int line, string kind) =>
        new(projectKey, path, line, kind);

    [Fact]
    public async Task WriteMapsAsync_RendersStructureWithoutVisibleTypeIdsAndPreservesDeclarations()
    {
        using var temp = TestTempDirectory.Create();
        var maps = CreateMaps();

        await MapsReportWriter.WriteMapsAsync(temp.DirectoryPath, maps, CancellationToken.None);

        var productionStructure = await ReadStructureAsync(temp.DirectoryPath, "production", "p-production");
        Assert.Contains("# Structure — `src/Domain/Domain.csproj`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("### `App`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("- `Runner` (Class): `src/Domain/Runner.Part1.cs:10`, `src/Domain/Runner.Part2.cs:30`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("- `Outer<T>.Inner<U>` (Class): `src/Domain/Outer.cs:8`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("### `App.Domain`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("- `Cache` (Class): `src/Domain/Cache.cs:4`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("- `Widget` (Class): `src/Domain/Widget.cs:2`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("### `Infrastructure`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("- `Cache` (Class): `src/Domain/Infrastructure/Cache.cs:5`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("- `Widget` (Class): `src/Domain/Unused.cs:1`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("### `Shared`", productionStructure, StringComparison.Ordinal);
        Assert.Contains("- `Token` (Class): `src/Domain/Token.cs:2`", productionStructure, StringComparison.Ordinal);

        foreach (var type in maps.Types.Where(static type => type.ProjectKey == "p-production"))
        {
            Assert.DoesNotContain(type.Id, productionStructure, StringComparison.Ordinal);
        }

        var sharedStructure = await ReadStructureAsync(temp.DirectoryPath, "production", "p-shared");
        Assert.Contains("### `Shared`", sharedStructure, StringComparison.Ordinal);
        Assert.Contains("- `Token` (Class): `src/Shared/Token.cs:2`", sharedStructure, StringComparison.Ordinal);
        Assert.Contains("- `OtherConsumer` (Class): `Shared.cs:12`", sharedStructure, StringComparison.Ordinal);
        foreach (var type in maps.Types.Where(static type => type.ProjectKey == "p-shared"))
        {
            Assert.DoesNotContain(type.Id, sharedStructure, StringComparison.Ordinal);
        }

        var testsStructure = await ReadStructureAsync(temp.DirectoryPath, "tests", "p-tests-one");
        Assert.Contains("### `App.Tests`", testsStructure, StringComparison.Ordinal);
        Assert.Contains("- `WidgetTests` (Class): `tests/Domain.Tests/WidgetTests.Part2.cs:20`, `tests/Domain.Tests/WidgetTests.cs:3`", testsStructure, StringComparison.Ordinal);
        Assert.DoesNotContain("t-test-one", testsStructure, StringComparison.Ordinal);
    }

    private static async Task<string> ReadDependenciesAsync(string root, string role, string key) =>
        await File.ReadAllTextAsync(Path.Combine(root, "maps", role, key, "dependencies.md"));

    private static Task<string> ReadDependencyPartAsync(string root, string role, string key, string fileName) =>
        ReadMapFileAsync(root, role, key, fileName);

    private static async Task<string> ReadMapFileAsync(string root, string role, string key, string fileName) =>
        await File.ReadAllTextAsync(Path.Combine(root, "maps", role, key, fileName));

    private static async Task<string> ReadStructureAsync(string root, string role, string key) =>
        await File.ReadAllTextAsync(Path.Combine(root, "maps", role, key, "structure.md"));

    private static string ExtractSection(string text, string heading, string? nextHeading)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected section '{heading}'.");
        var contentStart = start + heading.Length;
        var end = nextHeading is null ? text.Length : text.IndexOf(nextHeading, contentStart, StringComparison.Ordinal);
        Assert.True(nextHeading is null || end >= 0, $"Expected section boundary '{nextHeading}'.");
        return text[contentStart..(end < 0 ? text.Length : end)];
    }

    private static int CountOccurrences(string value, string search) =>
        (value.Length - value.Replace(search, string.Empty, StringComparison.Ordinal).Length) / search.Length;
}
