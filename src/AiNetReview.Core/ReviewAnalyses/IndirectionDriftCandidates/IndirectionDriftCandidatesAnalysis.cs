namespace AiNetReview.Core.ReviewAnalyses.IndirectionDriftCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using AiNetReview.Core.ReviewAnalyses;
using Microsoft.CodeAnalysis;

/// <summary>Reports current statically declared paths formed by transparent method forwarding.</summary>
public sealed class IndirectionDriftCandidatesAnalysis : IReviewAnalysis
{
    public ReviewAnalysisDescriptor Descriptor { get; } = new(
        analysisId: "indirection-drift-candidates",
        title: "Indirection Drift Candidates",
        behaviorVersion: 2,
        purpose: "Identifies current statically declared paths of transparent forwarding within production and test C# projects for human review.",
        measurement: "Reports one finding for each maximal root path with at least two transparent forwarding edges, three distinct containing types, and three distinct project-relative C# source files within one project. Targets are statically bound declarations in the loaded snapshot; runtime dispatch and historical growth are not measured.",
        reviewQuestions: ["What responsibility does each forwarding layer add, and is this path intentional for the architecture?"]);

    public async Task<ReviewAnalysisResult> ExecuteAsync(
        ReviewContext context,
        ReviewAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var paths = new List<ForwardingPath>();
        foreach (var project in context.Solution.Projects
                     .Where(static project => project.Language == LanguageNames.CSharp)
                     .OrderBy(static project => project.FilePath ?? project.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declarations = await TransparentForwardingClassifier.ClassifyProjectAsync(context, project, cancellationToken).ConfigureAwait(false);
            if (declarations.Count == 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(project.FilePath))
            {
                throw new AnalysisFailedException($"Project '{project.Name}' has no project file path.");
            }

            var projectPath = context.GetProjectRelativePath(project.FilePath);
            paths.AddRange(ForwardingPathGraph.SelectQualifyingPaths(declarations, projectPath, cancellationToken));
        }

        if (paths.Count == 0)
        {
            return ReviewAnalysisResult.Empty;
        }

        var findings = new List<FindingDraft>(paths.Count);
        foreach (var path in paths.OrderBy(static path => path.ProjectPath, StringComparer.Ordinal)
                     .ThenBy(static path => path.Members[0].SourcePath, StringComparer.Ordinal)
                     .ThenBy(static path => path.Members[0].DeclarationLine)
                     .ThenBy(static path => path.Members[0].DocId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            findings.Add(await CreateFindingAsync(path, cancellationToken).ConfigureAwait(false));
        }

        return new ReviewAnalysisResult(findings);
    }

    private static async Task<FindingDraft> CreateFindingAsync(ForwardingPath path, CancellationToken cancellationToken)
    {
        var evidence = new List<FindingEvidence>(path.Members.Count);
        var symbols = new List<FindingSymbol>(path.Members.Count);
        foreach (var member in path.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isForwarder = member.Target is not null;
            var line = isForwarder ? member.Target!.InvocationLine : member.DeclarationLine;
            var sourceText = await member.Document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (line < 1 || line > sourceText.Lines.Count)
            {
                throw new AnalysisFailedException($"Forwarding path line {line} is outside source '{member.SourcePath}'.");
            }

            var snippet = sourceText.Lines[line - 1].ToString().Trim();
            if (snippet.Length == 0)
            {
                throw new AnalysisFailedException($"Forwarding path source line {line} in '{member.SourcePath}' is empty.");
            }

            evidence.Add(new FindingEvidence(
                member.SourcePath,
                line,
                member.DocId,
                isForwarder ? $"Forwards to {member.Target!.DocId}." : "Ends the statically declared forwarding path.",
                snippet));
            symbols.Add(new FindingSymbol(path.ProjectPath, member.SourcePath, member.DocId, member.DeclarationLine));
        }

        var root = path.Members[0];
        return new FindingDraft(
            path.ProjectPath,
            root.SourcePath,
            root.DocId,
            "transparent-forwarding-path",
            root.DeclarationLine,
            $"This statically declared path contains {path.ForwardingEdgeCount} transparent forwarding edges across {path.DistinctTypeCount} types and {path.DistinctFileCount} files. Runtime dispatch and historical growth are not established. Review what responsibility each forwarding layer adds and whether the path is intentional for the architecture.",
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["forwardingEdgeCount"] = path.ForwardingEdgeCount,
                ["distinctTypeCount"] = path.DistinctTypeCount,
                ["distinctFileCount"] = path.DistinctFileCount,
            },
            evidence,
            symbols);
    }
}
