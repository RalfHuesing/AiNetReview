namespace AiNetReview.FastTests.Configuration;

using AiNetReview.Core.Configuration;

public sealed class SolutionDiscoveryTests
{
    [Fact]
    public void Discover_ReturnsNullWhenNoSolutionExistsAtRoot()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("nested/Hidden.sln", "");

        Assert.Null(new SolutionDiscovery().Discover(temp.DirectoryPath));
    }

    [Fact]
    public void Discover_ReturnsTheOnlyTopLevelSolution()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("Only.sln", "");

        Assert.Equal("Only.sln", new SolutionDiscovery().Discover(temp.DirectoryPath));
    }

    [Fact]
    public void Discover_PrefersProjectDirectoryNameBeforeSolutionFormat()
    {
        using var temp = TestTempDirectory.Create();
        var projectName = System.IO.Path.GetFileName(temp.DirectoryPath);
        temp.CreateFile("Other.slnx", "");
        temp.CreateFile($"{projectName}.sln", "");

        Assert.Equal($"{projectName}.sln", new SolutionDiscovery().Discover(temp.DirectoryPath));
    }

    [Fact]
    public void Discover_PrefersSlnxThenOrdinalFileNameWhenNoNameMatches()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("zeta.slnx", "");
        temp.CreateFile("alpha.slnx", "");
        temp.CreateFile("first.sln", "");

        Assert.Equal("alpha.slnx", new SolutionDiscovery().Discover(temp.DirectoryPath));
    }

    [Fact]
    public void Discover_UsesOrdinalFileNameWhenCandidatesHaveTheSameFormat()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("zeta.sln", "");
        temp.CreateFile("Alpha.sln", "");

        Assert.Equal("Alpha.sln", new SolutionDiscovery().Discover(temp.DirectoryPath));
    }
}
