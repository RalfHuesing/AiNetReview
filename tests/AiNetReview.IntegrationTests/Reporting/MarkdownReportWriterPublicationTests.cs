namespace AiNetReview.IntegrationTests.Reporting;

using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Reporting;
using AiNetReview.Core.ReviewAnalyses;

public sealed class MarkdownReportWriterPublicationTests
{
    [Fact]
    public async Task WriteAsync_PublishesUniqueConcurrentRunsAndPreservesEarlierRuns()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new PublicationAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty)]);
        var writer = new MarkdownReportWriter();

        var first = await writer.WriteAsync(config, result);
        var originalBytes = await File.ReadAllBytesAsync(Path.Combine(config.ResolvedOutputDirectory, first.RunId, "index.md"));
        var concurrent = await Task.WhenAll(writer.WriteAsync(config, result), writer.WriteAsync(config, result));

        Assert.Equal(3, new[] { first }.Concat(concurrent).Select(static report => report.RunId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(Path.Combine(config.ResolvedOutputDirectory, first.RunId, "index.md")));
        Assert.All(concurrent, report => Assert.True(File.Exists(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md"))));
        Assert.Empty(Directory.EnumerateDirectories(config.ResolvedOutputDirectory, ".ainetreview-tmp-*"));
    }

    [Fact]
    public async Task WriteAsync_RetriesPublicationAfterTransientWindowsFileLock()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TestTempDirectory.Create();
        var analysis = new PublicationAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty)]);
        FileStream? lockedFile = null;
        var moveAttempts = 0;
        var firstMoveError = 0;
        var writer = new MarkdownReportWriter(_ =>
        {
            var temporaryPath = Directory.EnumerateDirectories(config.ResolvedOutputDirectory, ".ainetreview-tmp-*").Single();
            lockedFile = new FileStream(Path.Combine(temporaryPath, "index.md"), FileMode.Open, FileAccess.Read, FileShare.None);
            return ValueTask.CompletedTask;
        }, (sourcePath, destinationPath) =>
        {
            moveAttempts++;
            if (moveAttempts == 1)
            {
                var exception = Assert.Throws<IOException>(() => Directory.Move(sourcePath, destinationPath));
                firstMoveError = exception.HResult;
                lockedFile!.Dispose();
                lockedFile = null;
                throw exception;
            }

            Assert.Null(lockedFile);
            Directory.Move(sourcePath, destinationPath);
        }, Directory.Delete);

        var report = await writer.WriteAsync(config, result);

        Assert.Equal(2, moveAttempts);
        Assert.Equal(unchecked((int)0x80070005), firstMoveError);
        Assert.True(File.Exists(Path.Combine(config.ResolvedOutputDirectory, report.RunId, "index.md")));
        Assert.Empty(Directory.EnumerateDirectories(config.ResolvedOutputDirectory, ".ainetreview-tmp-*"));
    }

    [Fact]
    public async Task WriteAsync_ExhaustedPublicationRetriesReattemptOwnedTemporaryCleanup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TestTempDirectory.Create();
        var analysis = new PublicationAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty)]);
        FileStream? lockedFile = null;
        var moveAttempts = 0;
        var deleteAttempts = 0;
        var writer = new MarkdownReportWriter(_ =>
        {
            var temporaryPath = Directory.EnumerateDirectories(config.ResolvedOutputDirectory, ".ainetreview-tmp-*").Single();
            lockedFile = new FileStream(Path.Combine(temporaryPath, "index.md"), FileMode.Open, FileAccess.Read, FileShare.None);
            return ValueTask.CompletedTask;
        }, (sourcePath, destinationPath) =>
        {
            moveAttempts++;
            Directory.Move(sourcePath, destinationPath);
        }, (path, recursive) =>
        {
            deleteAttempts++;
            if (deleteAttempts <= 2)
            {
                if (deleteAttempts == 2)
                {
                    lockedFile!.Dispose();
                    lockedFile = null;
                }

                throw new IOException("injected transient cleanup lock", unchecked((int)0x80070005));
            }

            Directory.Delete(path, recursive);
        });

        var exception = await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(config, result));

        Assert.Equal(unchecked((int)0x80070005), exception.HResult);
        Assert.Equal(5, moveAttempts);
        Assert.Equal(3, deleteAttempts);
        Assert.Null(lockedFile);
        Assert.Empty(Directory.EnumerateDirectories(config.ResolvedOutputDirectory, ".ainetreview-tmp-*"));
    }

    [Fact]
    public async Task WriteAsync_LeavesClearlyNamedTemporaryDirectoryWhenLockOutlastsCleanupRetries()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new PublicationAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty)]);
        var writer = new MarkdownReportWriter(
            static _ => ValueTask.CompletedTask,
            (_, _) => throw new IOException("persistent publication lock", unchecked((int)0x80070005)),
            (_, _) => throw new IOException("persistent cleanup lock", unchecked((int)0x80070005)));

        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(config, result));

        var leftovers = Directory.EnumerateDirectories(config.ResolvedOutputDirectory, ".ainetreview-tmp-*").ToArray();
        Assert.Single(leftovers);
        Assert.Empty(Directory.EnumerateDirectories(config.ResolvedOutputDirectory).Where(path =>
            !Path.GetFileName(path).StartsWith(".ainetreview-tmp-", StringComparison.Ordinal)));

        Directory.Delete(leftovers[0], recursive: true);
    }

    [Fact]
    public async Task WriteAsync_DoesNotRetryNonWin32MoveErrorsWithLockLikeLowWord()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new PublicationAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty)]);
        var moveAttempts = 0;
        var writer = new MarkdownReportWriter(
            static _ => ValueTask.CompletedTask,
            (_, _) =>
            {
                moveAttempts++;
                throw new IOException("unrelated HRESULT with a lock-like low word", unchecked((int)0x80010005));
            },
            Directory.Delete);

        var exception = await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(config, result));

        Assert.Equal(unchecked((int)0x80010005), exception.HResult);
        Assert.Equal(1, moveAttempts);
        Assert.Empty(Directory.EnumerateDirectories(config.ResolvedOutputDirectory, ".ainetreview-tmp-*"));
    }

    [Fact]
    public async Task WriteAsync_DoesNotRetryPermanentWin32MoveErrors()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new PublicationAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty)]);
        var moveAttempts = 0;
        var writer = new MarkdownReportWriter(
            static _ => ValueTask.CompletedTask,
            (_, _) =>
            {
                moveAttempts++;
                throw new IOException("permanent Win32 error", unchecked((int)0x80070003));
            },
            Directory.Delete);

        var exception = await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(config, result));

        Assert.Equal(unchecked((int)0x80070003), exception.HResult);
        Assert.Equal(1, moveAttempts);
        Assert.Empty(Directory.EnumerateDirectories(config.ResolvedOutputDirectory, ".ainetreview-tmp-*"));
    }

    [Fact]
    public async Task WriteAsync_CancellationDuringPublicationRetryStopsFurtherMovesAndCleansTemporaryReport()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new PublicationAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty)]);
        using var cancellation = new CancellationTokenSource();
        var moveAttempts = 0;
        var writer = new MarkdownReportWriter(
            static _ => ValueTask.CompletedTask,
            (_, _) =>
            {
                moveAttempts++;
                cancellation.Cancel();
                throw new IOException("cancel during transient retry", unchecked((int)0x80070005));
            },
            Directory.Delete);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(config, result, cancellation.Token));

        Assert.Equal(1, moveAttempts);
        Assert.Empty(Directory.EnumerateDirectories(config.ResolvedOutputDirectory, ".ainetreview-tmp-*"));
    }

    [Fact]
    public async Task WriteAsync_RemovesTemporaryReportWhenWritingFailsBeforePublication()
    {
        using var temp = TestTempDirectory.Create();
        var analysis = new PublicationAnalysis();
        var config = CreateConfig(temp.DirectoryPath, analysis);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, ReviewAnalysisResult.Empty)]);
        var writer = new MarkdownReportWriter(_ => ValueTask.FromException(new IOException("injected pre-publication failure")));

        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(config, result));

        Assert.Empty(Directory.EnumerateDirectories(config.ResolvedOutputDirectory));
        Assert.Empty(Directory.EnumerateFiles(config.ResolvedOutputDirectory));
    }

    [Fact]
    public async Task WriteAsync_CentralAuditReportsReferenceSourcesOutsideTheirOutputDirectory()
    {
        using var temp = TestTempDirectory.Create();
        var repositoryRoot = temp.GetPath("target-repository");
        Directory.CreateDirectory(repositoryRoot);
        var sourcePath = Path.Combine(repositoryRoot, "Sample Code.cs");
        await File.WriteAllTextAsync(sourcePath, "public sealed class Sample { }");
        await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "Sample.slnx"), "<Solution />");
        var analysis = new PublicationAnalysis();
        var standardConfig = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\"publication-analysis\":{}}}";
        var outputDirectory = temp.GetPath("central/audit-reporting/sample-repository");
        var config = new ReviewConfigValidator(new ReviewAnalysisRegistry([analysis]))
            .ValidateForAudit(repositoryRoot, standardConfig, outputDirectory);
        var finding = new AiNetReview.Core.Findings.FindingDraft(
            "Sample.csproj",
            "Sample Code.cs",
            "T:Sample",
            "type",
            1,
            "A test finding.",
            new Dictionary<string, double> { ["count"] = 1 },
            [new AiNetReview.Core.Findings.FindingEvidence("Sample Code.cs", 1, "Source", "Source evidence.", "Sample")]);
        var result = new ReviewRunResult([new ReviewAnalysisRunResult(analysis.Descriptor.AnalysisId, new ReviewAnalysisResult([finding]))]);

        var report = await new MarkdownReportWriter().WriteAsync(config, result);
        var analysisReportPath = Path.Combine(outputDirectory, report.RunId, "production",  "publication-analysis.md");
        var analysisReport = await File.ReadAllTextAsync(analysisReportPath);
        Assert.Contains("Total findings: 1", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Sample Code.cs", analysisReport, StringComparison.Ordinal);
        Assert.Contains("Sample", analysisReport, StringComparison.Ordinal);
        Assert.DoesNotContain("#L", analysisReport, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "Sample Code.cs")));
        Assert.False(Directory.Exists(Path.Combine(repositoryRoot, "reports")));
    }

    private static ReviewConfig CreateConfig(string root, PublicationAnalysis analysis)
    {
        File.WriteAllText(Path.Combine(root, "Sample.slnx"), "<Solution />");
        var json = "{\"schemaVersion\":1,\"solution\":\"Sample.slnx\",\"outputDirectory\":\"reports\",\"analyses\":{\""
            + analysis.Descriptor.AnalysisId + "\":{}}}";
        return new ReviewConfigValidator(new ReviewAnalysisRegistry([analysis])).Validate(root, json);
    }

    private sealed class PublicationAnalysis : IReviewAnalysis
    {
        internal PublicationAnalysis() => Descriptor = new ReviewAnalysisDescriptor(
            "publication-analysis", "Publication Review analysis", 1, "A test purpose.", "A test measurement.", ["Is publication complete?"]);

        public ReviewAnalysisDescriptor Descriptor { get; }

        public Task<AiNetReview.Core.ReviewAnalyses.ReviewAnalysisResult> ExecuteAsync(
            AiNetReview.Core.Analysis.ReviewContext context,
            ReviewAnalysisOptions options,
            CancellationToken cancellationToken) => Task.FromResult(ReviewAnalysisResult.Empty);
    }
}
