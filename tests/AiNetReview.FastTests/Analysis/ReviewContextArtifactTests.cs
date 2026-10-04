namespace AiNetReview.FastTests.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.FastTests;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;

public sealed class ReviewContextArtifactTests
{
    [Fact]
    public async Task TypeDependencyGraph_IsSharedWithinContextAndIsolatedAcrossContexts()
    {
        using var fixture = CreateFixture();
        var secondContext = new ReviewContext(fixture.Context.Solution, fixture.Context.ProjectRoot);

        var first = await fixture.Context.GetTypeDependencyGraphAsync();
        var second = await fixture.Context.GetTypeDependencyGraphAsync();
        var separateRun = await secondContext.GetTypeDependencyGraphAsync();

        Assert.Same(first, second);
        Assert.NotSame(first, separateRun);
        Assert.Equal(2, first.Nodes.Count(static node => node.Symbol.Name == "Example"));
    }

    [Fact]
    public async Task FindingSourceIndex_IsSharedAndRetainsProjectOwnershipAndText()
    {
        using var fixture = CreateFixture();

        var first = await fixture.Context.GetFindingSourceIndexAsync();
        var second = await fixture.Context.GetFindingSourceIndexAsync();

        Assert.Same(first, second);
        Assert.True(first.TryGetProjectOwnedSource("Sample/Sample.csproj", "Sample/Sample.cs", out var text));
        Assert.Contains("class Example", text.Text.ToString(), StringComparison.Ordinal);
        Assert.True(first.TryGetProjectOwnedSource("Other/Other.csproj", "Sample/Sample.cs", out var otherText));
        Assert.Contains("class Example", otherText.Text.ToString(), StringComparison.Ordinal);
        Assert.True(first.TryGetSolutionSources("Sample/Sample.cs", out var sources));
        Assert.Equal(2, sources.Count);
    }

    [Fact]
    public async Task CurrentFindingValidator_ValidatesTypedEvidenceRangeOwnershipAndBounds()
    {
        using var fixture = CreateFixture();
        var validator = new CurrentFindingValidator();
        var valid = CreateFinding(new FindingSourceRange("Sample/Sample.csproj", 1, 1, 1, 14));

        var validated = await validator.ValidateAndSortAsync("range-analysis", fixture.Context, [valid]);

        Assert.Same(valid, Assert.Single(validated));
        var wrongOwner = CreateFinding(new FindingSourceRange("Missing/Missing.csproj", 1, 1, 1, 14));
        await Assert.ThrowsAsync<AnalysisFailedException>(() => validator.ValidateAndSortAsync(
            "range-analysis", fixture.Context, [wrongOwner]));
        var outOfBounds = CreateFinding(new FindingSourceRange("Sample/Sample.csproj", 1, 1, 1, 1000));
        await Assert.ThrowsAsync<AnalysisFailedException>(() => validator.ValidateAndSortAsync(
            "range-analysis", fixture.Context, [outOfBounds]));
        var wrongRelatedSymbol = CreateFinding(
            new FindingSourceRange("Sample/Sample.csproj", 1, 1, 1, 14),
            new FindingSymbol("Other/Other.csproj", "Sample/Sample.cs", "Other", 1));
        await Assert.ThrowsAsync<AnalysisFailedException>(() => validator.ValidateAndSortAsync(
            "range-analysis", fixture.Context, [wrongRelatedSymbol]));
    }

    [Fact]
    public async Task RunScopedArtifact_CachesOnlySuccessfulBuildsAndRetriesAfterCancellation()
    {
        var artifact = new RunScopedArtifact<object>();
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => artifact.GetAsync(
            _ =>
            {
                attempts++;
                throw new InvalidOperationException("build failed");
            }, CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => artifact.GetAsync(
            async token =>
            {
                attempts++;
                await cancellation.CancelAsync();
                return new object();
            }, cancellation.Token));

        var successful = new object();
        var built = await artifact.GetAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(successful);
            }, CancellationToken.None);
        var cached = await artifact.GetAsync(
            _ => throw new InvalidOperationException("cached artifact should be used"), CancellationToken.None);

        Assert.Same(successful, built);
        Assert.Same(successful, cached);
        Assert.Equal(3, attempts);

        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => artifact.GetAsync(
            _ => throw new InvalidOperationException("cancelled callers must not receive a cached artifact"),
            alreadyCancelled.Token));
    }

    [Fact]
    public async Task RunScopedArtifact_SerializesConcurrentBuildRequests()
    {
        var artifact = new RunScopedArtifact<object>();
        var attempts = 0;
        var expected = new object();

        async Task<object> BuildAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(20, cancellationToken);
            return expected;
        }

        var results = await Task.WhenAll(
            artifact.GetAsync(BuildAsync, CancellationToken.None),
            artifact.GetAsync(BuildAsync, CancellationToken.None));

        Assert.All(results, result => Assert.Same(expected, result));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RunScopedArtifact_CanceledWaiterDoesNotCancelSharedBuilder()
    {
        var artifact = new RunScopedArtifact<object>();
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new object();
        async Task<object> BuildAsync(CancellationToken cancellationToken)
        {
            buildStarted.SetResult();
            await releaseBuild.Task.WaitAsync(cancellationToken);
            return expected;
        }

        var firstCaller = artifact.GetAsync(BuildAsync, CancellationToken.None);
        await buildStarted.Task;
        using var waitingCancellation = new CancellationTokenSource();
        var cancelledWaiter = artifact.GetAsync(BuildAsync, waitingCancellation.Token);
        await waitingCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);

        releaseBuild.SetResult();
        Assert.Same(expected, await firstCaller);
        Assert.Same(expected, await artifact.GetAsync(
            _ => throw new InvalidOperationException("successful shared build should be cached"), CancellationToken.None));
    }

    private static Fixture CreateFixture()
    {
        var workspace = new FastTestWorkspace();
        var projectId = workspace.AddProject("Sample", projectFilePath: Path.Combine(workspace.RootPath, "Sample", "Sample.csproj"));
        var otherProjectId = workspace.AddProject("Other", projectFilePath: Path.Combine(workspace.RootPath, "Other", "Other.csproj"));
        var sharedSourcePath = Path.Combine(workspace.RootPath, "Sample", "Sample.cs");
        workspace.AddDocument(
            projectId,
            "Sample.cs",
            "namespace Sample; public class Example { public Item Value { get; } = new(); } public class Item { }",
            sharedSourcePath);
        workspace.AddDocument(otherProjectId, "Sample.cs", "namespace Sample; public class Other { }", sharedSourcePath);
        return new Fixture(workspace, workspace.CreateReviewContext());
    }

    private static FindingDraft CreateFinding(FindingSourceRange range, FindingSymbol? evidenceSymbol = null) => new(
        "Sample/Sample.csproj",
        "Sample/Sample.cs",
        "Example",
        "range",
        1,
        "Review the source range.",
        new Dictionary<string, double>(),
        [new FindingEvidence("Sample/Sample.cs", 1, "Example", "Source declaration", "class Example", evidenceSymbol, range)]);

    private sealed class Fixture(FastTestWorkspace workspace, ReviewContext context) : IDisposable
    {
        public ReviewContext Context { get; } = context;

        public void Dispose() => workspace.Dispose();
    }
}
