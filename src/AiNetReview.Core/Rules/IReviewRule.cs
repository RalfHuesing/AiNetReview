namespace AiNetReview.Core.Rules;

using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;

public interface IReviewRule
{
    RuleDescriptor Descriptor { get; }

    Task<RuleResult> ExecuteAsync(
        ReviewContext context,
        RuleOptions options,
        CancellationToken cancellationToken);
}
