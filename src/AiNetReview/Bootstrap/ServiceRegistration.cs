namespace AiNetReview.Bootstrap;

using AiNetReview.Core.Catalog;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.TemplateNoOp;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddAiNetReviewServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<RuleRegistry>();
        services.AddSingleton<CatalogWriter>();
        return services;
    }

    public static IServiceCollection AddAiNetReviewRules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IReviewRule, TemplateNoOpRule>();
        return services;
    }
}
