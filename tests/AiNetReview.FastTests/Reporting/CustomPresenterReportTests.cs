namespace AiNetReview.FastTests.Reporting;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Findings;
using AiNetReview.Core.Reporting;
using AiNetReview.Core.ReviewAnalyses;

public sealed class CustomPresenterReportTests
{
    [Fact]
    public async Task WriteAsync_EscapesCustomPresenterLabelsAndHeadingsButKeepsRawMarkdownNotes()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new CustomPresenterAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        const string projectPath = "Sample/Sample.csproj";
        const string sourcePath = "src/Example.cs";
        var symbol = new FindingSymbol(projectPath, sourcePath, "M:Sample.Run", 4);
        var testSymbol = new FindingSymbol("Other/Other.csproj", "src/Other.cs", "M:Sample.Other", 8);
        var finding = new FindingDraft(projectPath, sourcePath, symbol.SymbolId, "custom", 4,
            "Review this custom presentation.", new Dictionary<string, double>(),
            [new FindingEvidence(sourcePath, 4, "Run", "detail", "void Run();")], [symbol], [symbol]);
        var reviewFinding = new ReviewFinding(analysis.Descriptor.AnalysisId, finding, [sourcePath], [])
        {
            Occurrences = [new ReviewFindingOccurrence(symbol, ProjectRole.Production), new ReviewFindingOccurrence(testSymbol, ProjectRole.Tests)],
            SubjectOccurrences = [new ReviewFindingOccurrence(symbol, ProjectRole.Production)],
        };
        var result = new ReviewRunResult([
            new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding])),
        ])
        {
            Findings = [reviewFinding],
            ProjectClassifications = [
                new ProjectClassification(projectPath, ProjectRole.Production, ProjectClassificationReason.NoTestMarker),
                new ProjectClassification(testSymbol.ProjectPath, ProjectRole.Tests, ProjectClassificationReason.ProjectNameSuffix),
            ],
            Maps = new ReviewMaps([], [], [], []),
        };

        var published = await new MarkdownReportWriter().WriteAsync(config, result);
        var reportPath = Path.Combine(config.ResolvedOutputDirectory, published.RunId, "production", "custom-presenter.md");
        var report = await File.ReadAllTextAsync(reportPath);

        Assert.Contains("Signal \\| \\*\\*bold\\*\\* \\[link\\]\\+  \\#\\# fake: body text", report, StringComparison.Ordinal);
        Assert.Contains("- Evidence \\| \\*\\*heading\\*\\* \\[x\\]\\+  \\#\\#\\# injected", report, StringComparison.Ordinal);
        Assert.Contains("- Path \\| \\*\\*label\\*\\* \\[y\\]\\+  \\#\\# injected:", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\n## fake", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\n### injected", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\n## injected", report, StringComparison.Ordinal);
        Assert.Contains(CustomPresenterAnalysis.RawMarkdownNote, report, StringComparison.Ordinal);
        Assert.Contains("Sample.Run", report, StringComparison.Ordinal);
        Assert.Contains("`src/Other.cs:8`", report, StringComparison.Ordinal);
        Assert.Contains("[`2:3`–`2:9`)", report, StringComparison.Ordinal);
    }

    private static ReviewConfig CreateConfig(string root, IReviewAnalysis analysis)
    {
        File.WriteAllText(Path.Combine(root, "Sample.slnx"), "<Solution />");
        var config = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"custom-presenter\":{}}}";
        return new ReviewConfigValidator(new ReviewAnalysisRegistry([analysis])).Validate(root, config);
    }

    private sealed class CustomPresenterAnalysis : IReviewAnalysis
    {
        public const string RawMarkdownNote = "\n## Presenter-owned raw note\n\n- Raw | markdown remains intentional.\n";

        public ReviewAnalysisDescriptor Descriptor { get; } = new(
            "custom-presenter", "Custom presenter", 1, "Custom report rendering.", "Custom rendering fixture.", ["Review the custom result?"]);

        public IReviewFindingPresenter FindingPresenter { get; } = new CustomPresenter();

        public Task<ReviewAnalysisResult> ExecuteAsync(ReviewContext context, ReviewAnalysisOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(ReviewAnalysisResult.Empty);

        private sealed class CustomPresenter : IReviewFindingPresenter
        {
            public ReviewAnalysisPresentation PresentAnalysis(IReadOnlyList<ReviewFinding> findings) =>
                new([RawMarkdownNote]);

            public ReviewFindingPresentation PresentFinding(ReviewFinding finding, bool suppressAttributionUncertainty) => new(
            [
                new ReviewFindingReportTextBlock("Signal | **bold** [link]+ \n## fake", "body text"),
                new ReviewFindingReportEvidenceBlock("Evidence | **heading** [x]+ \n### injected", [
                    new ReviewFindingReportEvidence("src/Other.cs", 8, "M:Sample.Other", "Custom evidence.",
                        "Other/Other.csproj", new FindingSourceRange("Other/Other.csproj", 2, 3, 2, 9),
                        ShowProjectPathWhenDifferent: true, ShowSourcePathWhenDifferent: true, IncludeOccurrenceRole: true),
                ]),
                new ReviewFindingReportSymbolsBlock([
                    new ReviewFindingReportSymbol(new FindingSymbol("Other/Other.csproj", "src/Other.cs", "M:Sample.Other", 8),
                        ShowProjectPathWhenDifferent: true, IncludeOccurrenceRole: true),
                ]),
                new ReviewFindingReportPathBlock("Path | **label** [y]+ \n## injected", [
                    new ReviewFindingReportPathItem("M:Sample.Other", "src/Other.cs", 8, IncludeOccurrenceRole: true),
                ]),
            ], EvidenceIsRepresented: true);
        }
    }
}
