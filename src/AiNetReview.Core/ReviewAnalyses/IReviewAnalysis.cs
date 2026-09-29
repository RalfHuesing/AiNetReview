namespace AiNetReview.Core.ReviewAnalyses;

using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;

public interface IReviewAnalysis
{
    ReviewAnalysisDescriptor Descriptor { get; }

    Task<ReviewAnalysisResult> ExecuteAsync(
        ReviewContext context,
        ReviewAnalysisOptions options,
        CancellationToken cancellationToken);
}
