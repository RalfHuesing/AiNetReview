namespace AiNetReview.Core.Rules.TemplateNoOp;

using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Analysis;

public sealed class TemplateNoOpRule : IReviewRule
{
    public RuleDescriptor Descriptor { get; } = new(
        ruleId: "template-noop",
        title: "Template No-Op",
        behaviorVersion: 1,
        purpose: "Provides a registered rule extension point without assessing code quality.",
        measurement: "No code is measured.",
        reviewQuestions: ["Is the technical template still the intended active rule?"] ,
        isTemplate: true);

    public Task<RuleResult> ExecuteAsync(
        ReviewContext context,
        RuleOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RuleResult.Empty);
    }
}
