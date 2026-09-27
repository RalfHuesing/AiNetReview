namespace AiNetReview.IntegrationTests.FixtureRules;

using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Rules;

public sealed class FixtureFindingRule : IReviewRule
{
    public RuleDescriptor Descriptor { get; } = new(
        ruleId: "fixture-finding",
        title: "Fixture Finding",
        behaviorVersion: 1,
        purpose: "Provides a test-only rule registration for integration scenarios.",
        measurement: "No test findings are produced by this registration placeholder.",
        reviewQuestions: ["Is the test fixture configured for the intended scenario?"],
        options: [RuleOptionDescriptor.String("scenario", "Fixture scenario", "base")]);

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
