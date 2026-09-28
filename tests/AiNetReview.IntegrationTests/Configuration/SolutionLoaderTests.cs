namespace AiNetReview.IntegrationTests.Configuration;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;
using AiNetReview.Core.Rules;
using AiNetReview.Core.Rules.MethodControlFlowOutliers;

public sealed class SolutionLoaderTests
{
    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public async Task LoadAsync_LoadsAndCompilesSupportedSolutions(string extension)
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, extension, "namespace Sample; public sealed class SampleType { }");
        Assert.False(Directory.Exists(Path.Combine(root, ".git")));
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson($"Sample{extension}"));

        using var loaded = await new SolutionLoader().LoadAsync(config);

        var project = Assert.Single(loaded.Solution.Projects);
        Assert.Equal("Sample", project.Name);
    }

    [Fact]
    public async Task LoadAsync_ReportsCompilationErrorsAsAnalysisFailure()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(
            temp,
            ".slnx",
            "namespace Sample; public sealed class Broken { public int Value => Missing; }");
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson("Sample.slnx"));

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(() => new SolutionLoader().LoadAsync(config));

        Assert.Contains("compilation error", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_ReportsMissingProjectReferencesAsAnalysisFailure()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(
            temp,
            ".slnx",
            "namespace Sample; public sealed class SampleType { public Missing.Library.Type Value { get; } = new(); }",
            addMissingReference: true);
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson("Sample.slnx"));

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(() => new SolutionLoader().LoadAsync(config));

        Assert.Contains("compilation error", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_MapsMalformedSolutionToAnalysisFailure()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, ".slnx", "namespace Sample; public sealed class SampleType { }");
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson("Sample.slnx"));
        await File.WriteAllTextAsync(config.ResolvedSolutionPath, "<Solution><Project>");

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(() => new SolutionLoader().LoadAsync(config));

        Assert.Contains("loaded", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_RejectsSolutionWithoutCSharpProjects()
    {
        using var temp = TestTempDirectory.Create();
        var root = temp.GetPath("empty-solution");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Empty.slnx"), "<Solution />");
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson("Empty.slnx"));

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(() => new SolutionLoader().LoadAsync(config));

        Assert.Contains("does not contain a C# project", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_MaterializesSourceTextBeforeReturningImmutableSolution()
    {
        using var temp = TestTempDirectory.Create();
        const string originalSource = "namespace Sample; public sealed class OriginalType { }";
        var root = await CreateProjectAsync(temp, ".slnx", originalSource);
        var sourcePath = Path.Combine(root, "Sample", "Class1.cs");
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson("Sample.slnx"));

        using var loaded = await new SolutionLoader().LoadAsync(config);
        var document = Assert.Single(
            Assert.Single(loaded.Solution.Projects).Documents,
            candidate => string.Equals(candidate.FilePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class ChangedType { }");

        var loadedText = await document.GetTextAsync();

        Assert.Equal(originalSource, loadedText.ToString());
    }

    [Fact]
    public async Task LoadAsync_RejectsSourceIncludedFromOutsideProjectRoot()
    {
        using var temp = TestTempDirectory.Create();
        var outsideSource = temp.CreateFile("outside.cs", "namespace Sample; public sealed class ExternalSource { }");
        var root = await CreateProjectAsync(
            temp,
            ".slnx",
            "namespace Sample; public sealed class SampleType { }",
            outsideSource);
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson("Sample.slnx"));

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(() => new SolutionLoader().LoadAsync(config));

        Assert.Contains("outside the project root", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_RejectsSourceIncludedFromOutputDirectory()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(
            temp,
            ".slnx",
            "namespace Sample; public sealed class SampleType { }",
            sourceInOutputDirectory: true);
        var config = new ReviewConfigValidator(Registry()).Validate(root, ConfigurationJson("Sample.slnx", "Sample/reports"));

        var error = await Assert.ThrowsAsync<InvalidReviewInputException>(() => new SolutionLoader().LoadAsync(config));

        Assert.Contains("must not contain C# source files", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_CapturesProjectMarkupOutsideRoslynAdditionalDocuments()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, ".slnx", "namespace Sample; public sealed class SampleType { }");
        var markupPath = Path.Combine(root, "Sample", "Views", "Page.razor");
        Directory.CreateDirectory(Path.GetDirectoryName(markupPath)!);
        await File.WriteAllTextAsync(markupPath, "<button @onclick=\"Save\">Save</button>");
        var config = Config(root, markupRule: true);

        using var loaded = await new SolutionLoader().LoadAsync(config);

        Assert.DoesNotContain(Assert.Single(loaded.Solution.Projects).AdditionalDocuments,
            document => string.Equals(document.FilePath, markupPath, StringComparison.OrdinalIgnoreCase));
        var snapshot = Assert.Single(loaded.MarkupDocuments);
        Assert.Equal(markupPath, snapshot.FilePath);
        Assert.Equal("<button @onclick=\"Save\">Save</button>", snapshot.Text);
    }

    [Fact]
    public async Task LoadAsync_MarkupSnapshotDoesNotChangeWhenFileChangesAfterLoad()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, ".slnx", "namespace Sample; public sealed class SampleType { }");
        var markupPath = Path.Combine(root, "Sample", "View.xaml");
        await File.WriteAllTextAsync(markupPath, "<Window Title=\"Original\" />");
        var config = Config(root, markupRule: true);

        using var loaded = await new SolutionLoader().LoadAsync(config);
        await File.WriteAllTextAsync(markupPath, "<Window Title=\"Changed\" />");

        Assert.Equal("<Window Title=\"Original\" />", Assert.Single(loaded.MarkupDocuments).Text);
    }

    [Fact]
    public async Task ReviewRunner_ProvidesLoadedMarkupSnapshotToRules()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, ".slnx", "namespace Sample; public sealed class SampleType { }");
        await File.WriteAllTextAsync(Path.Combine(root, "Sample", "View.razor"), "snapshot content");
        var rule = new MarkupFixtureRule();
        var config = new ReviewConfigValidator(new RuleRegistry([new MethodControlFlowOutliersRule(), rule]))
            .Validate(root, ConfigurationJson("Sample.slnx", rules: "\"dead-code-candidates\": {}"));
        using var loaded = await new SolutionLoader().LoadAsync(config);

        await new ReviewRunner().RunAsync(config, loaded);

        Assert.Equal("snapshot content", Assert.Single(rule.MarkupDocuments).Text);
    }

    [Fact]
    public async Task LoadAsync_DoesNotCaptureMarkupWithoutConfiguredRule()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, ".slnx", "namespace Sample; public sealed class SampleType { }");
        var markupPath = Path.Combine(root, "Sample", "View.js");
        await File.WriteAllTextAsync(markupPath, new string('x', 1024 * 1024 + 1));
        var config = Config(root, markupRule: false);

        using var loaded = await new SolutionLoader().LoadAsync(config);

        Assert.Empty(loaded.MarkupDocuments);
    }

    [Fact]
    public async Task LoadAsync_SkipsBuildDependencyAndNestedProjectDirectories()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, ".slnx", "namespace Sample; public sealed class SampleType { }");
        var projectRoot = Path.Combine(root, "Sample");
        var includedPath = Path.Combine(projectRoot, "Views", "Page.razor");
        Directory.CreateDirectory(Path.GetDirectoryName(includedPath)!);
        await File.WriteAllTextAsync(includedPath, "included");
        foreach (var directory in new[] { "bin", "obj", "node_modules", ".git", "nested" })
        {
            var skipped = Path.Combine(projectRoot, directory);
            Directory.CreateDirectory(skipped);
            await File.WriteAllTextAsync(Path.Combine(skipped, "Page.razor"), "skipped");
        }

        await File.WriteAllTextAsync(Path.Combine(projectRoot, "nested", "Foreign.csproj"), "<Project />");
        using var loaded = await new SolutionLoader().LoadAsync(Config(root, markupRule: true));

        var markup = Assert.Single(loaded.MarkupDocuments);
        Assert.Equal(includedPath, markup.FilePath);
        Assert.Equal("included", markup.Text);
    }

    [Fact]
    public async Task LoadAsync_DoesNotFollowReparsePointDirectory()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, ".slnx", "namespace Sample; public sealed class SampleType { }");
        var projectRoot = Path.Combine(root, "Sample");
        var outside = temp.GetPath("outside-markup");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "Outside.razor"), "outside");
        var link = Path.Combine(projectRoot, "linked-markup");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            if (!OperatingSystem.IsWindows() || !TryCreateJunction(link, outside))
            {
                throw Xunit.Sdk.SkipException.ForSkip("The test host cannot create a reparse-point directory.");
            }
        }

        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint);

        using var loaded = await new SolutionLoader().LoadAsync(Config(root, markupRule: true));

        Assert.Empty(loaded.MarkupDocuments);
    }

    [Fact]
    public async Task LoadAsync_FailsWhenMarkupCannotBeRead()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, ".slnx", "namespace Sample; public sealed class SampleType { }");
        var markupPath = Path.Combine(root, "Sample", "Locked.razor");
        await File.WriteAllTextAsync(markupPath, "locked");
        var config = Config(root, markupRule: true);
        await using var locked = new FileStream(markupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(() => new SolutionLoader().LoadAsync(config));

        Assert.Contains("markup file could not be read", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_FailsWhenMarkupFileCountExceedsLimit()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, ".slnx", "namespace Sample; public sealed class SampleType { }");
        var markupRoot = Path.Combine(root, "Sample", "Markup");
        Directory.CreateDirectory(markupRoot);
        for (var index = 0; index <= 2_000; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(markupRoot, $"{index:D4}.js"), "x");
        }

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(
            () => new SolutionLoader().LoadAsync(Config(root, markupRule: true)));

        Assert.Contains("2,000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_FailsWhenMarkupFileExceedsByteLimit()
    {
        using var temp = TestTempDirectory.Create();
        var root = await CreateProjectAsync(temp, ".slnx", "namespace Sample; public sealed class SampleType { }");
        await File.WriteAllTextAsync(Path.Combine(root, "Sample", "Oversized.js"), new string('x', 1024 * 1024 + 1));

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(
            () => new SolutionLoader().LoadAsync(Config(root, markupRule: true)));

        Assert.Contains("1 MiB", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_FailsWhenAdditionalMarkupPathIsOutsideProjectRoot()
    {
        using var temp = TestTempDirectory.Create();
        var outsideMarkup = temp.CreateFile("external.razor", "outside");
        var root = await CreateProjectAsync(
            temp,
            ".slnx",
            "namespace Sample; public sealed class SampleType { }",
            externalMarkup: outsideMarkup);

        var error = await Assert.ThrowsAsync<AnalysisFailedException>(
            () => new SolutionLoader().LoadAsync(Config(root, markupRule: true)));

        Assert.Contains("outside", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ReviewConfig Config(string root, bool markupRule) =>
        new ReviewConfigValidator(Registry(markupRule)).Validate(
            root,
            ConfigurationJson("Sample.slnx", rules: markupRule ? "\"dead-code-candidates\": {}" : null));

    private static RuleRegistry Registry(bool includeMarkupRule = false) => includeMarkupRule
        ? new RuleRegistry([new MethodControlFlowOutliersRule(), new MarkupFixtureRule()])
        : new RuleRegistry([new MethodControlFlowOutliersRule()]);

    private static string ConfigurationJson(string solution, string outputDirectory = "reports", string? rules = null) => $$"""
        {
          "schemaVersion": 1,
          "solution": "{{solution}}",
          "outputDirectory": "{{outputDirectory}}",
          "rules": { {{(rules is null ? "\"method-control-flow-outliers\": {}" : rules)}} }
        }
        """;

    private static async Task<string> CreateProjectAsync(
        TestTempDirectory temp,
        string solutionExtension,
        string source,
        string? externalSource = null,
        bool addMissingReference = false,
        bool sourceInOutputDirectory = false,
        string? externalMarkup = null)
    {
        var root = temp.GetPath("mini-project");
        var projectDirectory = Path.Combine(root, "Sample");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
        var externalItem = externalSource is null
            ? string.Empty
            : $"<ItemGroup><Compile Include=\"{SecurityElementEscape(Path.GetRelativePath(projectDirectory, externalSource))}\" /></ItemGroup>";
        var missingReference = addMissingReference
            ? "<ItemGroup><Reference Include=\"Missing.Library\" /></ItemGroup>"
            : string.Empty;
        var externalMarkupItem = externalMarkup is null
            ? string.Empty
            : $"<ItemGroup><AdditionalFiles Include=\"{SecurityElementEscape(externalMarkup)}\" /></ItemGroup>";
        await File.WriteAllTextAsync(projectFile,
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>{externalItem}{missingReference}{externalMarkupItem}</Project>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Class1.cs"), source);
        if (sourceInOutputDirectory)
        {
            var outputDirectory = Path.Combine(projectDirectory, "reports");
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "Included.cs"), "namespace Sample; public sealed class Included { }");
        }
        await RestoreAsync(projectFile, projectDirectory);

        var solutionPath = Path.Combine(root, "Sample" + solutionExtension);
        if (solutionExtension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"Sample/Sample.csproj\" /></Solution>");
        }
        else
        {
            const string projectGuid = "{18C3BBA5-AB06-4CB3-A205-CF3122FFCD43}";
            await File.WriteAllTextAsync(solutionPath, $$"""
                Microsoft Visual Studio Solution File, Format Version 12.00
                # Visual Studio Version 17
                VisualStudioVersion = 17.0.31903.59
                MinimumVisualStudioVersion = 10.0.40219.1
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Sample", "Sample\Sample.csproj", "{{projectGuid}}"
                EndProject
                Global
                 GlobalSection(SolutionConfigurationPlatforms) = preSolution
                  Debug|Any CPU = Debug|Any CPU
                 EndGlobalSection
                 GlobalSection(ProjectConfigurationPlatforms) = postSolution
                  {{projectGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                  {{projectGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
                 EndGlobalSection
                EndGlobal
                """);
        }

        return root;
    }

    private static async Task RestoreAsync(string projectFile, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add("--ignore-failed-sources");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet restore.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout + await stderr;
        Assert.True(process.ExitCode == 0, $"dotnet restore failed: {output}");
    }

    private static string SecurityElementEscape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static bool TryCreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private sealed class MarkupFixtureRule : IReviewRule
    {
        public IReadOnlyList<MarkupDocumentSnapshot> MarkupDocuments { get; private set; } = Array.Empty<MarkupDocumentSnapshot>();

        public RuleDescriptor Descriptor { get; } = new(
            "dead-code-candidates",
            "Fixture",
            1,
            "Fixture rule for loader snapshot tests.",
            "Fixture behavior.",
            ["Is the snapshot available?"]);

        public Task<RuleResult> ExecuteAsync(
            ReviewContext context,
            RuleOptions options,
            System.Threading.CancellationToken cancellationToken)
        {
            MarkupDocuments = context.MarkupDocuments;
            return Task.FromResult(RuleResult.Empty);
        }
    }
}
