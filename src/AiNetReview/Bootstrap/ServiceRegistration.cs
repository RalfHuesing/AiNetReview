namespace AiNetReview.Bootstrap;

using AiNetReview.Core.ReviewAnalyses;
using AiNetReview.Core.ReviewAnalyses.MethodControlFlowOutliers;
using AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;
using AiNetReview.Core.ReviewAnalyses.DuplicateCodeCandidates;
using AiNetReview.Core.ReviewAnalyses.NonAsciiIdentifiers;
using AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;
using AiNetReview.Core.ReviewAnalyses.MissingTestEvidenceCandidates;
using AiNetReview.Core.ReviewAnalyses.CodeSizeCandidates;
using AiNetReview.Core.ReviewAnalyses.StructuralDuplicationCandidates;
using AiNetReview.Core.ReviewAnalyses.TypeDependencyCycleCandidates;
using AiNetReview.Core.ReviewAnalyses.TypeDependencyHubCandidates;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Reporting;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddAiNetReviewServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ReviewAnalysisRegistry>();
        services.AddSingleton<SolutionDiscovery>();
        services.AddSingleton<DefaultReviewConfigGenerator>();
        services.AddSingleton<ReviewConfigValidator>();
        services.AddSingleton<SolutionLoader>();
        services.AddSingleton<ReviewRunner>();
        services.AddSingleton<MarkdownReportWriter>();
        return services;
    }

    public static IServiceCollection AddAiNetReviewAnalyses(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IReviewAnalysis, MethodControlFlowOutliersAnalysis>();
        services.AddSingleton<IReviewAnalysis, DeadCodeCandidatesAnalysis>();
        services.AddSingleton<IReviewAnalysis, DuplicateCodeCandidatesAnalysis>();
        services.AddSingleton<IReviewAnalysis, NonAsciiIdentifiersAnalysis>();
        services.AddSingleton<IReviewAnalysis, IndirectionDriftCandidatesAnalysis>();
        services.AddSingleton<IReviewAnalysis, MissingTestEvidenceCandidatesAnalysis>();
        services.AddSingleton<IReviewAnalysis, CodeSizeCandidatesAnalysis>();
        services.AddSingleton<IReviewAnalysis, StructuralDuplicationCandidatesAnalysis>();
        services.AddSingleton<IReviewAnalysis, TypeDependencyCycleCandidatesAnalysis>();
        services.AddSingleton<IReviewAnalysis, TypeDependencyHubCandidatesAnalysis>();
        return services;
    }
}
