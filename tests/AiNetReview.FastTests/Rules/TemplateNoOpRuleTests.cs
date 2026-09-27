namespace AiNetReview.FastTests.Rules;

using System;
using System.Threading;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.TemplateNoOp;
using Microsoft.CodeAnalysis;

public sealed class TemplateNoOpRuleTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsCompleteEmptyResult()
    {
        using var workspace = new AdhocWorkspace();
        using var tempDirectory = TestTempDirectory.Create();
        var rule = new TemplateNoOpRule();
        var context = new ReviewContext(workspace.CurrentSolution, tempDirectory.DirectoryPath);

        var result = await rule.ExecuteAsync(context, rule.Descriptor.ResolveOptions(), CancellationToken.None);

        Assert.Empty(result.Findings);
        Assert.Same(RuleResult.Empty, result);
        Assert.Equal("template-noop", rule.Descriptor.RuleId);
        Assert.Equal(1, rule.Descriptor.BehaviorVersion);
        Assert.Empty(rule.Descriptor.Options);
        Assert.True(rule.Descriptor.IsTemplate);
    }

    [Fact]
    public async Task ExecuteAsync_ObservesCancellation()
    {
        using var workspace = new AdhocWorkspace();
        using var tempDirectory = TestTempDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TemplateNoOpRule().ExecuteAsync(
            new ReviewContext(workspace.CurrentSolution, tempDirectory.DirectoryPath),
            new TemplateNoOpRule().Descriptor.ResolveOptions(),
            cancellation.Token));
    }
}
