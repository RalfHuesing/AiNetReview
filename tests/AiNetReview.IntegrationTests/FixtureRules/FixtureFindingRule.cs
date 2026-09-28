namespace AiNetReview.IntegrationTests.FixtureRules;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using AiNetReview.Core.Rules;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed class FixtureFindingRule : IReviewRule
{
    public FixtureFindingRule(int behaviorVersion = 1)
    {
        Descriptor = new RuleDescriptor(
            ruleId: "fixture-finding",
            title: "Fixture Finding",
            behaviorVersion: behaviorVersion,
            purpose: "Provides a test-only rule for verifying current finding reports.",
            measurement: "Emits one finding for each named fixture method.",
            reviewQuestions: ["Does this fixture method still require review?"],
            options: [RuleOptionDescriptor.String(
                "scenario",
                "Fixture scenario",
                "base",
                static value => !string.IsNullOrWhiteSpace(value))]);
    }

    public RuleDescriptor Descriptor { get; }

    public async Task<RuleResult> ExecuteAsync(
        ReviewContext context,
        RuleOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        var scenario = options["scenario"].GetString() ?? "base";
        if (scenario == "none")
        {
            return RuleResult.Empty;
        }

        var findings = new List<FindingDraft>();
        foreach (var document in context.Solution.Projects
                     .SelectMany(static project => project.Documents)
                     .Where(static document => document.FilePath is not null)
                     .OrderBy(static document => document.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                throw new InvalidOperationException("Fixture source syntax could not be loaded.");
            }

            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var projectPath = ToRelativePath(context.ProjectRoot, document.Project.FilePath
                ?? throw new InvalidOperationException($"Fixture project '{document.Project.Name}' has no physical path."));
            var sourcePath = ToRelativePath(context.ProjectRoot, document.FilePath!);
            var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Fixture project '{document.Project.Name}' has no compilation.");
            var semanticModel = compilation.GetSemanticModel(root.SyntaxTree);
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                         .Where(static method => method.Identifier.ValueText.StartsWith("FixtureCase", StringComparison.Ordinal))
                         .OrderBy(static method => method.Identifier.ValueText, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = method.Identifier.ValueText;
                var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                var subjectId = symbol is null ? null : Microsoft.CodeAnalysis.DocumentationCommentId.CreateDeclarationId(symbol);
                if (string.IsNullOrWhiteSpace(subjectId))
                {
                    throw new InvalidOperationException($"Fixture method '{name}' has no stable symbol identity.");
                }

                var line = method.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                findings.Add(new FindingDraft(
                    projectPath,
                    sourcePath,
                    subjectId,
                    name.ToLowerInvariant(),
                    line,
                    $"Fixture scenario '{scenario}' requires review of {name}.",
                    new Dictionary<string, double> { ["caseCount"] = 1 },
                    [new FindingEvidence(sourcePath, line, "Fixture case", name, text.Lines[line - 1].ToString())]));
            }
        }

        return new RuleResult(findings);
    }

    private static string ToRelativePath(string projectRoot, string path) =>
        Path.GetRelativePath(projectRoot, path).Replace(Path.DirectorySeparatorChar, '/');
}
