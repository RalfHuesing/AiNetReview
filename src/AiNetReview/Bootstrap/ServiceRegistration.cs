namespace AiNetReview.Bootstrap;

using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.TemplateNoOp;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Reporting;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddAiNetReviewServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<RuleRegistry>();
        services.AddSingleton<ReviewConfigValidator>();
        services.AddSingleton<SolutionLoader>();
        services.AddSingleton<ReviewRunner>();
        services.AddSingleton<MarkdownReportWriter>();
        return services;
    }

    public static IServiceCollection AddAiNetReviewRules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IReviewRule, TemplateNoOpRule>();
        return services;
    }
}
