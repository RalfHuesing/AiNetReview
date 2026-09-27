namespace AiNetReview.FastTests;

using System;
using System.Threading.Tasks;

public sealed class TestKitTests
{
    [Fact]
    public async Task TestWaiter_SucceedsWhenConditionBecomesTrue()
    {
        var count = 0;
        await TestWaiter.WaitForConditionAsync(() => ++count >= 3, TimeSpan.FromSeconds(2));
        Assert.True(count >= 3);
    }

    [Fact]
    public void IsolatedFixtureLease_LooksForFixturesInIntegrationTestsDirectory()
    {
        var solutionRoot = SolutionRootLocator.Find();
        var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            IsolatedFixtureLease.CopyFixture(solutionRoot, "non-existent-fixture-12345"));

        var expectedRelative = Path.Combine("tests", "AiNetReview.IntegrationTests", "Fixtures", "non-existent-fixture-12345");
        Assert.Contains(expectedRelative, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsolatedFixtureLease_CopiesFixtureExcludingBinAndObj()
    {
        var solutionRoot = SolutionRootLocator.Find();
        var fixtureDir = Path.Combine(solutionRoot, "tests", "AiNetReview.IntegrationTests", "Fixtures", "test-dummy");
        Directory.CreateDirectory(fixtureDir);
        var sourceFile = Path.Combine(fixtureDir, "test.txt");
        File.WriteAllText(sourceFile, "content");
        var binDir = Path.Combine(fixtureDir, "bin");
        Directory.CreateDirectory(binDir);
        var binFile = Path.Combine(binDir, "artifact.dll");
        File.WriteAllText(binFile, "bin");

        try
        {
            using var lease = IsolatedFixtureLease.CopyFixture(solutionRoot, "test-dummy");
            Assert.True(File.Exists(Path.Combine(lease.RootPath, "test.txt")));
            Assert.False(File.Exists(Path.Combine(lease.RootPath, "bin", "artifact.dll")));
        }
        finally
        {
            if (Directory.Exists(fixtureDir))
            {
                Directory.Delete(fixtureDir, recursive: true);
            }
        }
    }
}
