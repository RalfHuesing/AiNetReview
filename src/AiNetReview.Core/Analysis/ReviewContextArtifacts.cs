namespace AiNetReview.Core.Analysis;

using System;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Findings;

/// <summary>Builds review-context artifacts through their explicitly owned typed slots.</summary>
internal static class ReviewContextArtifacts
{
    public static Task<TypeDependencyGraph> GetTypeDependencyGraphAsync(
        this ReviewContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TypeDependencyGraphArtifact.GetAsync(
            token => TypeDependencyGraphBuilder.BuildAsync(context, token), cancellationToken);
    }

    public static Task<FindingSourceIndex> GetFindingSourceIndexAsync(
        this ReviewContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.FindingSourceIndexArtifact.GetAsync(
            token => FindingSourceIndex.BuildAsync(context, token), cancellationToken);
    }
}
