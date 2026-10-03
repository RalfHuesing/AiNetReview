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
    public async Task WriteMapsAsync_GroupsRepeatedWitnessPathsAndIncomingConsumersWithoutLosingEdgeEvidence()
    {
        using var temp = TestTempDirectory.Create();
        var projects = new[]
        {
            new ReviewMapProject("p-production", "src/Product.csproj", ProjectRole.Production, ProjectClassificationReason.NoTestMarker, []),
            new ReviewMapProject("p-tests-one", "tests/Product.Tests.csproj", ProjectRole.Tests, ProjectClassificationReason.ProjectNameSuffix, []),
            new ReviewMapProject("p-tests-two", "tests/Other.Tests.csproj", ProjectRole.Tests, ProjectClassificationReason.ProjectNameSuffix, []),
        };
        var types = new[]
        {
            Type("t-a", "p-production"), Type("t-b", "p-production"), Type("t-c", "p-production"), Type("t-d", "p-production"),
            Type("t-test1", "p-tests-one"), Type("t-test1b", "p-tests-one"), Type("t-test2", "p-tests-two"),
        };
        var edges = new[]
        {
            Edge("t-a", "t-b", false, Witness("p-production", "src/Product.cs", 10, "MemberUse"),
                Witness("p-production", "src/Product.cs", 10, "ExplicitTypeUse"), Witness("p-production", "src/Product.cs", 11, "MemberUse")),
            Edge("t-a", "t-c", false, Witness("p-production", "src/Product.cs", 20, "Inheritance")),
            Edge("t-a", "t-d", false, Witness("p-production", "src/Other.cs", 3, "MemberUse")),
            Edge("t-b", "t-a", false, Witness("p-production", "src/Product.cs", 40, "MemberUse")),
            Edge("t-c", "t-a", false, Witness("p-production", "src/Product.cs", 41, "Inheritance")),
            Edge("t-test1", "t-a", true, Witness("p-tests-one", "tests/ProductTests.cs", 4, "MemberUse")),
            Edge("t-test1b", "t-a", true, Witness("p-tests-one", "tests/ProductTests.cs", 7, "ExplicitTypeUse")),
            Edge("t-test2", "t-a", true, Witness("p-tests-two", "tests/OtherTests.cs", 5, "MemberUse")),
        };
        var maps = new ReviewMaps(projects, [], types, edges);

        await MapsReportWriter.WriteMapsAsync(temp.DirectoryPath, maps, CancellationToken.None);

        var mapsDirectory = Path.Combine(temp.DirectoryPath, "maps");
        var index = await File.ReadAllTextAsync(Path.Combine(mapsDirectory, "index.md"));
        Assert.Contains("incoming edges can include production sources and test consumers", index, StringComparison.Ordinal);
        Assert.Contains("Test-to-test and production-to-test edges are outside this graph.", index, StringComparison.Ordinal);

        var production = await File.ReadAllTextAsync(Path.Combine(mapsDirectory, "production", "p-production", "dependencies.md"));
        Assert.Contains("- `t-a` → `t-b`: L10 ExplicitTypeUse, L10 MemberUse, L11 MemberUse", production, StringComparison.Ordinal);
        Assert.Contains("- `t-a` → `t-c`: L20 Inheritance", production, StringComparison.Ordinal);
        Assert.Contains("- `t-a` → `t-d`: L3 MemberUse", production, StringComparison.Ordinal);
        Assert.Contains("### `src/Product.cs`", production, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(production, "### `src/Product.cs`"));
        Assert.Contains("### `src/Other.cs`", production, StringComparison.Ordinal);
        Assert.Contains("`t-b`", production, StringComparison.Ordinal);
        Assert.Contains("`t-c`", production, StringComparison.Ordinal);
        Assert.Contains("L40 MemberUse", production, StringComparison.Ordinal);
        Assert.Contains("L41 Inheritance", production, StringComparison.Ordinal);
        Assert.Contains("`t-test1`", production, StringComparison.Ordinal);
        Assert.Contains("`t-test1b`", production, StringComparison.Ordinal);
        Assert.Contains("`t-test2`", production, StringComparison.Ordinal);
        Assert.Contains("| `p-tests-one` | `t-test1`, `t-test1b` | `../../tests/p-tests-one/structure.md` |", production, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(production, "`../../tests/p-tests-one/structure.md`"));
        Assert.Equal(1, CountOccurrences(production, "`../../tests/p-tests-two/structure.md`"));
        Assert.Equal(1, CountOccurrences(production, "`../../tests/p-tests-one/dependencies.md`"));
        Assert.Equal(1, CountOccurrences(production, "`../../tests/p-tests-two/dependencies.md`"));
        Assert.Contains("- `t-a` ← `t-b`, `t-c`", production, StringComparison.Ordinal);

        var testsOne = await File.ReadAllTextAsync(Path.Combine(mapsDirectory, "tests", "p-tests-one", "dependencies.md"));
        Assert.Equal(1, CountOccurrences(testsOne, "### `tests/ProductTests.cs`"));
        Assert.Contains("- `t-test1` → `t-a`: L4 MemberUse", testsOne, StringComparison.Ordinal);
        Assert.Contains("- `t-test1b` → `t-a`: L7 ExplicitTypeUse", testsOne, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(testsOne, "`../../production/p-production/structure.md`"));
    }

    private static ReviewMapType Type(string id, string projectKey) =>
        new(id, projectKey, "Sample", id, "Class", "Sample." + id,
            [new ReviewMapTypeDeclaration(projectKey + ".cs", 1)]);

    private static ReviewMapTypeEdge Edge(string from, string to, bool isTestContext, params ReviewMapTypeWitness[] witnesses) =>
        new(from, to, isTestContext, witnesses);

    private static ReviewMapTypeWitness Witness(string projectKey, string path, int line, string kind) =>
        new(projectKey, path, line, kind);

    private static int CountOccurrences(string value, string search) =>
        (value.Length - value.Replace(search, string.Empty, StringComparison.Ordinal).Length) / search.Length;
}
