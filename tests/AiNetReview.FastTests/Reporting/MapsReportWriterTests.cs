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
    public async Task WriteMapsAsync_RendersNamedEdgesWithCompleteWitnessesAndRoleSpecificIncomingConsumers()
    {
        using var temp = TestTempDirectory.Create();
        var maps = CreateMaps();

        await MapsReportWriter.WriteMapsAsync(temp.DirectoryPath, maps, CancellationToken.None);

        var production = await ReadDependenciesAsync(temp.DirectoryPath, "production", "p-production");
        Assert.Contains("# Type dependencies", production, StringComparison.Ordinal);
        Assert.Contains("## Project routes", production, StringComparison.Ordinal);
        Assert.Contains("| Project path | Role | Structure route | Dependency route |", production, StringComparison.Ordinal);
        Assert.Contains("## Outgoing edges", production, StringComparison.Ordinal);
        Assert.Contains("## Incoming edges", production, StringComparison.Ordinal);
        Assert.Contains("| `tests/Domain.Tests/Domain.Tests.csproj` | tests | `../../tests/p-tests-one/structure.md` | `../../tests/p-tests-one/dependencies.md` |", production, StringComparison.Ordinal);
        Assert.Contains("| `src/Shared/Shared.csproj` | production | `../../production/p-shared/structure.md` | `../../production/p-shared/dependencies.md` |", production, StringComparison.Ordinal);
        Assert.Contains("`global::App.Domain.Widget`", production, StringComparison.Ordinal);
        Assert.Contains("`global::App.Domain.Cache`", production, StringComparison.Ordinal);
        Assert.Contains("`global::Infrastructure.Cache`", production, StringComparison.Ordinal);
        Assert.Contains("Outer<T>.Inner<U>", production, StringComparison.Ordinal);
        Assert.Contains("global::Shared.Token (ProjectPath: src/Shared/Shared.csproj)", production, StringComparison.Ordinal);
        Assert.Contains("tests/Domain.Tests/Domain.Tests.csproj", production, StringComparison.Ordinal);
        Assert.Contains("src/Domain/Domain.csproj", production, StringComparison.Ordinal);
        Assert.Contains("tests", production, StringComparison.Ordinal);
        Assert.Contains("production", production, StringComparison.Ordinal);
        Assert.Contains("src/Domain/Runner.Part1.cs:10", production, StringComparison.Ordinal);
        Assert.Contains("src/Domain/Runner.Part2.cs:30", production, StringComparison.Ordinal);
        Assert.Contains("src/Domain/Runner.Part1.cs", production, StringComparison.Ordinal);
        Assert.Contains("src/Domain/Runner.Part2.cs", production, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(production, "### `src/Domain/Runner.Part1.cs`"));
        Assert.Equal(1, CountOccurrences(production, "### `src/Domain/Runner.Part2.cs`"));
        Assert.Contains("- `Runner` → `global::App.Domain.Widget`: L10 ExplicitTypeUse, L10 MemberUse, L11 MemberUse", production, StringComparison.Ordinal);
        Assert.Contains("- `Runner` → `global::App.Domain.Cache`: L15 Inheritance", production, StringComparison.Ordinal);
        Assert.Contains("- `Runner` → `global::Infrastructure.Cache`: L30 MemberUse, L31 MemberUse", production, StringComparison.Ordinal);
        Assert.Contains("- `Runner` → `Outer<T>.Inner<U>`: L42 Inheritance", production, StringComparison.Ordinal);
        Assert.Contains("- `Runner` → `global::Shared.Token (ProjectPath: src/Shared/Shared.csproj)`: L44 MemberUse", production, StringComparison.Ordinal);
        var consumerDeclarations = ExtractSection(production, "## Consumer declarations", "## Incoming edges");
        var incomingEdges = ExtractSection(production, "## Incoming edges", null);
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
        Assert.DoesNotContain("#### Production consumers", production, StringComparison.Ordinal);
        Assert.DoesNotContain("##### `src/Shared/Shared.csproj`", production, StringComparison.Ordinal);
        Assert.DoesNotContain("None.", incomingEdges, StringComparison.Ordinal);

        Assert.DoesNotContain("t-runner", production, StringComparison.Ordinal);
        Assert.DoesNotContain("t-widget", production, StringComparison.Ordinal);
        Assert.DoesNotContain("t-cache-domain", production, StringComparison.Ordinal);
        Assert.DoesNotContain("t-cache-infra", production, StringComparison.Ordinal);
        Assert.DoesNotContain("t-inner", production, StringComparison.Ordinal);
        Assert.DoesNotContain("t-token-local", production, StringComparison.Ordinal);
        Assert.DoesNotContain("t-test-one", production, StringComparison.Ordinal);
        Assert.DoesNotContain("t-test-two", production, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreign type IDs", production, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<table", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{\"", production, StringComparison.Ordinal);

        Assert.Equal(1, CountOccurrences(production, "`../../tests/p-tests-one/dependencies.md`"));
        Assert.Equal(1, CountOccurrences(production, "`../../tests/p-tests-two/dependencies.md`"));

        var tests = await ReadDependenciesAsync(temp.DirectoryPath, "tests", "p-tests-one");
        Assert.Contains("WidgetTests", tests, StringComparison.Ordinal);
        Assert.Contains("- `WidgetTests` → `Widget`: L7 ExplicitTypeUse", tests, StringComparison.Ordinal);
        Assert.DoesNotContain("t-test-one", tests, StringComparison.Ordinal);
        Assert.DoesNotContain("t-widget", tests, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteMapsAsync_UsesShortNamesOnlyWhenUniqueAcrossLocalAndForeignTypes()
    {
        using var temp = TestTempDirectory.Create();
        await MapsReportWriter.WriteMapsAsync(temp.DirectoryPath, CreateMaps(), CancellationToken.None);

        var production = await ReadDependenciesAsync(temp.DirectoryPath, "production", "p-production");

        Assert.Contains("`global::App.Domain.Widget`", production, StringComparison.Ordinal);
        Assert.Contains("`global::App.Domain.Cache`", production, StringComparison.Ordinal);
        Assert.Contains("`global::Infrastructure.Cache`", production, StringComparison.Ordinal);
        Assert.Contains("src/Domain/Domain.csproj", production, StringComparison.Ordinal);
        Assert.Contains("tests/Domain.Tests/Domain.Tests.csproj", production, StringComparison.Ordinal);
        Assert.DoesNotContain("t-", production, StringComparison.Ordinal);
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

        foreach (var (role, key) in new[] { ("production", "p-production"), ("tests", "p-tests-one"), ("tests", "p-tests-two") })
        {
            Assert.Equal(
                await ReadDependenciesAsync(first.DirectoryPath, role, key),
                await ReadDependenciesAsync(second.DirectoryPath, role, key));
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
        Assert.Contains("No in-scope direct type edges", empty, StringComparison.Ordinal);
        Assert.Contains("`Source` → `Target`", connected, StringComparison.Ordinal);
        Assert.Contains("without retained witnesses", connected, StringComparison.Ordinal);
        Assert.DoesNotContain("t-source", connected, StringComparison.Ordinal);
        Assert.DoesNotContain("t-target", connected, StringComparison.Ordinal);
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

    private static async Task<string> ReadDependenciesAsync(string root, string role, string key) =>
        await File.ReadAllTextAsync(Path.Combine(root, "maps", role, key, "dependencies.md"));

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
