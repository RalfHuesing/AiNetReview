namespace AiNetReview.FastTests.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Findings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

public sealed class AuditFindingPackagesTests
{
    [Fact]
    public async Task Build_AssignsEveryFindingOnceWithStableIdsAndPreservesOriginalOccurrences()
    {
        using var fixture = CreateFixture();
        var sourceContext = await AuditSourceContext.CreateAsync(fixture.Context);
        var widget = TypeId(sourceContext, "Product.Widget");
        var widgetRun = MemberId("M:Product.Widget.Run");
        var otherRun = MemberId("M:Product.Other.Run");
        var testRun = MemberId("M:Product.Tests.WidgetTests.Run");
        var unrelated = MemberId("M:Product.WidgetExtra.Missing");
        var findings = new[]
        {
            CreateFinding("code-size-candidates", "src/Product.csproj", "src/Product.cs", "type-size", widget,
                [("src/Product.cs", widget, 2, ProjectRole.Production)]),
            CreateFinding("method-control-flow-outliers", "src/Product.csproj", "src/Product.cs", "member-size", widgetRun,
                [("src/Product.cs", widgetRun, 3, ProjectRole.Production)]),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "test", testRun,
                [("tests/Product.Tests/Tests.cs", testRun, 4, ProjectRole.Tests)]),
            CreateFinding("duplicate-code-candidates", "src/Product.csproj", "src/Product.cs", "multi", "cluster",
                [("src/Product.cs", widgetRun, 3, ProjectRole.Production), ("src/Product.cs", otherRun, 6, ProjectRole.Production)]),
            CreateFinding("fixture-analysis", "src/Product.csproj", "src/Product.cs", "unknown", unrelated,
                [("src/Product.cs", unrelated, 8, ProjectRole.Production)]),
            CreateFinding("code-size-candidates", "src/Product.csproj", "src/Product.cs", "file-size", "file:src/Product.cs",
                [("src/Product.cs", "file:src/Product.cs", 1, ProjectRole.Production)]),
        };

        var first = AuditFindingPackages.Build(findings, sourceContext, hasCSharpSnapshotChanges: true);
        var shuffled = AuditFindingPackages.Build(findings.Reverse().ToArray(), sourceContext, hasCSharpSnapshotChanges: true);
        var reorderedSubjects = AuditFindingPackages.Build(findings.Select(finding => finding.Finding.Discriminator == "multi"
            ? finding with { SubjectOccurrences = finding.SubjectOccurrences.Reverse().ToArray() }
            : finding).ToArray(), sourceContext, hasCSharpSnapshotChanges: true);
        var empty = AuditFindingPackages.Build(Array.Empty<ReviewFinding>(), sourceContext, hasCSharpSnapshotChanges: true);

        Assert.Equal(findings.Length, first.AllFindings.FindingCount);
        Assert.Equal(findings.Length, first.AllFindings.Packages.Sum(static package => package.Findings.Count));
        Assert.All(first.AllFindings.Packages.SelectMany(static package => package.Findings), packaged =>
        {
            Assert.Same(findings.Single(finding => finding.AnalysisId == packaged.Finding.AnalysisId
                && finding.Finding.SubjectId == packaged.Finding.Finding.SubjectId), packaged.Finding);
            var original = findings.Single(finding => finding.AnalysisId == packaged.Finding.AnalysisId
                && finding.Finding.SubjectId == packaged.Finding.Finding.SubjectId);
            Assert.Equal(original.SubjectOccurrences, packaged.Finding.SubjectOccurrences);
        });
        Assert.Equal(first.AllFindings.Packages.Select(static package => package.Id).Order(StringComparer.Ordinal),
            shuffled.AllFindings.Packages.Select(static package => package.Id).Order(StringComparer.Ordinal));
        Assert.Equal(first.AllFindings.Packages.SelectMany(static package => package.Findings).Select(static finding => finding.Id).Order(StringComparer.Ordinal),
            shuffled.AllFindings.Packages.SelectMany(static package => package.Findings).Select(static finding => finding.Id).Order(StringComparer.Ordinal));
        Assert.Equal(first.AllFindings.Packages.SelectMany(static package => package.Findings).Select(static finding => finding.Id).Order(StringComparer.Ordinal),
            reorderedSubjects.AllFindings.Packages.SelectMany(static package => package.Findings).Select(static finding => finding.Id).Order(StringComparer.Ordinal));
        Assert.Empty(empty.AllFindings.Packages);
        Assert.Equal(0, empty.AllFindings.FindingCount);
        Assert.Equal(sourceContext.Types.Count(static type => !type.IsGenerated && type.Id == type.OuterTypeId)
            + sourceContext.Files.Count(static file => !file.IsGenerated), empty.AllFindings.Areas.Count);

        var typePackage = Assert.Single(first.AllFindings.Packages.Where(package => package.Findings.Any(item => item.Finding.Finding.SubjectId == widget)));
        Assert.Contains(typePackage.TestTypes, type => type.TypeId == "tests/Product.Tests/Product.Tests.csproj|Product.Tests.Tests.WidgetTests" ||
            sourceContext.Types.Any(sourceType => sourceType.Id == type.TypeId && sourceType.Name == "WidgetTests"));
        var assignedTestFinding = Assert.Single(typePackage.Findings.Where(static item => item.Finding.Finding.Discriminator == "test"));
        var testAssignment = Assert.Single(assignedTestFinding.Assignments);
        Assert.Equal(typePackage.Areas.Single().Id, testAssignment.AreaId);
        Assert.Equal("tests/Product.Tests/Product.Tests.csproj", testAssignment.ProjectPath);
        Assert.Equal("tests/Product.Tests/Tests.cs", testAssignment.SourcePath);
        Assert.Equal(4, testAssignment.Line);
        Assert.Equal(ProjectRole.Tests, testAssignment.Role);
        var multi = Assert.Single(first.AllFindings.Packages.SelectMany(static package => package.Findings)
            .Where(static packaged => packaged.Finding.Finding.Discriminator == "multi"));
        Assert.Equal(2, multi.PrimaryAreaIds.Count);
        Assert.Equal(2, multi.Assignments.Count);
        Assert.Equal(2, multi.Assignments.Select(static assignment => assignment.AreaId).Distinct(StringComparer.Ordinal).Count());

        var fileFinding = Assert.Single(first.AllFindings.Packages.SelectMany(static package => package.Findings)
            .Where(static packaged => packaged.Finding.Finding.Discriminator == "file-size"));
        var filePackage = Assert.Single(first.AllFindings.Packages.Where(package => package.Findings.Contains(fileFinding)));
        Assert.Equal("src/Product.cs", Assert.Single(filePackage.Areas).FilePath);
        Assert.Contains(first.AllFindings.ContextAreas, context => context.PackageId == filePackage.Id
            && context.Area.Name == "WidgetExtra" && context.IsContextOnly);
        Assert.Contains(typePackage.DirectReferences, reference => reference.SourceTypeId == typePackage.Areas.Single().TypeId
            && reference.TargetTypeId == typePackage.Areas.Single().TypeId);
        Assert.DoesNotContain(typePackage.Id, typePackage.RelatedPackageIds);
        Assert.Contains(typePackage.DirectReferences, static reference => reference.SourceTypeId is null
            && reference.SourcePath == "tests/Product.Tests/Tests.cs");
        Assert.Contains(first.AllFindings.ContextAreas, context => context.PackageId == typePackage.Id
            && context.Area.FilePath == "tests/Product.Tests/Tests.cs");
        var unresolved = Assert.Single(first.AllFindings.Packages.SelectMany(static package => package.Findings)
            .Where(static packaged => packaged.Finding.Finding.Discriminator == "unknown"));
        Assert.Equal("src/Product.cs", Assert.Single(first.AllFindings.Packages.Single(package => package.Findings.Contains(unresolved)).Areas).FilePath);
    }

    [Fact]
    public async Task Build_IsolatesTestTypesAndUsesSameSnapshotSelectionForBothViews()
    {
        using var fixture = CreateFixture();
        var sourceContext = await AuditSourceContext.CreateAsync(fixture.Context);
        var widgetId = MemberId("M:Product.Widget.Run");
        var otherId = MemberId("M:Product.Other.Run");
        var widgetTestId = MemberId("M:Product.Tests.WidgetTests.Run");
        var otherTestId = MemberId("M:Product.Tests.OtherTests.Run");
        var uncertainTestId = MemberId("M:Product.Tests.UncertainTests.Run");
        var partialTestId = MemberId("M:Product.Tests.PartialTests.Run");
        var partialSingleTestId = MemberId("M:Product.Tests.PartialSingleTests.Run");
        var partialUncertainTestId = MemberId("M:Product.Tests.PartialUncertainTests.Run");
        var noReferenceTestId = MemberId("M:Product.Tests.NoReferenceTests.Run");
        var sharedHelperTestId = MemberId("M:Product.Tests.SharedHelper.Run");
        var callerWidgetTestId = MemberId("M:Product.Tests.CallerWidget.Run");
        var callerOtherTestId = MemberId("M:Product.Tests.CallerOther.Run");
        var fileContextId = "file:tests/Product.Tests/TopLevel.cs";
        var singleTestFileId = "file:tests/Product.Tests/SingleTest.cs";
        var manyTestFileId = "file:tests/Product.Tests/Tests.cs";
        var singleFileId = MemberId("file:src/Single.cs");
        var findings = new[]
        {
            CreateFinding("fixture-analysis", "src/Product.csproj", "src/Product.cs", "widget", widgetId, [("src/Product.cs", widgetId, 3, ProjectRole.Production)], isChanged: false),
            CreateFinding("fixture-analysis", "src/Product.csproj", "src/Product.cs", "other", otherId, [("src/Product.cs", otherId, 7, ProjectRole.Production)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "widget-test", widgetTestId, [("tests/Product.Tests/Tests.cs", widgetTestId, 3, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "other-test", otherTestId, [("tests/Product.Tests/Tests.cs", otherTestId, 4, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "uncertain-test", uncertainTestId, [("tests/Product.Tests/Tests.cs", uncertainTestId, 5, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "partial-test", partialTestId, [("tests/Product.Tests/Tests.cs", partialTestId, 6, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "partial-single-test", partialSingleTestId, [("tests/Product.Tests/Tests.cs", partialSingleTestId, 7, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "partial-uncertain-test", partialUncertainTestId, [("tests/Product.Tests/Tests.cs", partialUncertainTestId, 8, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "no-reference-test", noReferenceTestId, [("tests/Product.Tests/Tests.cs", noReferenceTestId, 7, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "shared-helper-test", sharedHelperTestId, [("tests/Product.Tests/Tests.cs", sharedHelperTestId, 9, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "caller-widget-test", callerWidgetTestId, [("tests/Product.Tests/Tests.cs", callerWidgetTestId, 10, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "caller-other-test", callerOtherTestId, [("tests/Product.Tests/Tests.cs", callerOtherTestId, 11, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "property-only", MemberId("T:Product.Tests.PropertyOnlyTests"), [("tests/Product.Tests/Tests.cs", MemberId("T:Product.Tests.PropertyOnlyTests"), 6, ProjectRole.Tests)], isChanged: false),
            CreateFinding("fixture-analysis", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/TopLevel.cs", "file-context", fileContextId, [("tests/Product.Tests/TopLevel.cs", fileContextId, 1, ProjectRole.Tests)], isChanged: false),
            CreateFinding("code-size-candidates", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/SingleTest.cs", "single-test-file", singleTestFileId, [("tests/Product.Tests/SingleTest.cs", singleTestFileId, 1, ProjectRole.Tests)], isChanged: false),
            CreateFinding("code-size-candidates", "tests/Product.Tests/Product.Tests.csproj", "tests/Product.Tests/Tests.cs", "many-test-file", manyTestFileId, [("tests/Product.Tests/Tests.cs", manyTestFileId, 1, ProjectRole.Tests)], isChanged: false),
            CreateFinding("code-size-candidates", "src/Product.csproj", "src/Single.cs", "single-file", singleFileId, [("src/Single.cs", singleFileId, 1, ProjectRole.Production)], isChanged: false),
            CreateFinding("missing-test-evidence-candidates", "src/Product.csproj", "src/Product.cs", "snapshot-test-path", widgetId, [("src/Product.cs", widgetId, 3, ProjectRole.Production)], isChanged: false),
        };

        var views = AuditFindingPackages.Build(findings, sourceContext, hasCSharpSnapshotChanges: true);
        var unchangedSnapshot = AuditFindingPackages.Build(findings, sourceContext, hasCSharpSnapshotChanges: false);
        var noBaseline = AuditFindingPackages.Build(findings, sourceContext, hasCSharpSnapshotChanges: null);

        Assert.Equal(findings.Length, views.AllFindings.FindingCount);
        Assert.Equal(findings.Length, views.AllFindings.Packages.Sum(static package => package.Findings.Count));
        Assert.Equal(1, views.ChangedFiles.FindingCount);
        Assert.Equal("snapshot-test-path", Assert.Single(views.ChangedFiles.Packages.SelectMany(static package => package.Findings)).Finding.Finding.Discriminator);
        Assert.Empty(unchangedSnapshot.ChangedFiles.Packages);
        Assert.Single(noBaseline.ChangedFiles.Packages.SelectMany(static package => package.Findings));

        var packagesByFinding = views.AllFindings.Packages.SelectMany(package => package.Findings.Select(finding => (finding, package)))
            .ToDictionary(static pair => pair.finding.Finding.Finding.Discriminator, static pair => pair.package, StringComparer.Ordinal);
        Assert.Equal(packagesByFinding["widget"].Id, packagesByFinding["widget-test"].Id);
        Assert.Equal(packagesByFinding["other"].Id, packagesByFinding["other-test"].Id);
        Assert.NotEqual(packagesByFinding["widget-test"].Id, packagesByFinding["other-test"].Id);
        Assert.NotEqual(packagesByFinding["widget-test"].Id, packagesByFinding["uncertain-test"].Id);
        Assert.NotEqual(packagesByFinding["widget-test"].Id, packagesByFinding["partial-test"].Id);

        Assert.Contains(sourceContext.Types, type => type.Name == "PropertyOnlyTests"
            && sourceContext.Projects.Single(project => project.ProjectId == type.ProjectId).Role == ProjectRole.Tests);
        var uncertainAssignment = packagesByFinding["uncertain-test"].TestTypes.Single(type => type.TypeId.Contains("UncertainTests", StringComparison.Ordinal));
        Assert.True(uncertainAssignment.HasBindingUncertainty);
        var partialAssignment = packagesByFinding["partial-test"].TestTypes.Single(type => type.TypeId.Contains("PartialTests", StringComparison.Ordinal));
        Assert.Equal(2, partialAssignment.Declarations.Count);
        Assert.Equal(2, partialAssignment.ProductionContextAreaIds.Count);
        var partialSingleAssignment = packagesByFinding["partial-single-test"].TestTypes.Single(type => type.TypeId.Contains("PartialSingleTests", StringComparison.Ordinal));
        Assert.Equal(packagesByFinding["widget"].Areas.Single(static area => area.Name == "Widget").Id, partialSingleAssignment.AreaId);
        Assert.Equal(2, partialSingleAssignment.Declarations.Count);
        var partialUncertainAssignment = packagesByFinding["partial-uncertain-test"].TestTypes.Single(type => type.TypeId.Contains("PartialUncertainTests", StringComparison.Ordinal));
        Assert.True(partialUncertainAssignment.HasBindingUncertainty);
        Assert.NotEqual(packagesByFinding["widget"].Id, packagesByFinding["partial-uncertain-test"].Id);
        Assert.NotEqual(packagesByFinding["widget"].Id, packagesByFinding["no-reference-test"].Id);
        Assert.NotEqual(packagesByFinding["widget-test"].Id, packagesByFinding["no-reference-test"].Id);
        Assert.NotEqual(packagesByFinding["shared-helper-test"].Id, packagesByFinding["caller-widget-test"].Id);
        Assert.NotEqual(packagesByFinding["shared-helper-test"].Id, packagesByFinding["caller-other-test"].Id);
        Assert.NotEqual(packagesByFinding["caller-widget-test"].Id, packagesByFinding["caller-other-test"].Id);
        Assert.Contains(packagesByFinding["shared-helper-test"].Id, packagesByFinding["caller-widget-test"].RelatedPackageIds);
        Assert.DoesNotContain(packagesByFinding["widget"].Id, packagesByFinding["caller-widget-test"].RelatedPackageIds);
        Assert.DoesNotContain(packagesByFinding["widget"].Id, packagesByFinding["widget"].RelatedPackageIds);
        var singleFilePackage = packagesByFinding["single-file"];
        Assert.Equal(TypeId(sourceContext, "Product.Single"), Assert.Single(singleFilePackage.Areas).TypeDocumentationIds.Single());
        var contextTest = packagesByFinding["widget"].TestTypes.Single(type => type.TypeId.Contains("ContextTests", StringComparison.Ordinal));
        Assert.Contains("one direct production type reference", contextTest.Reason, StringComparison.Ordinal);
        var propertyOnlyType = sourceContext.Types.Single(type => type.Name == "PropertyOnlyTests");
        var propertyOnlyAssignments = views.AllFindings.Packages.SelectMany(static package => package.TestTypes)
            .Where(type => type.TypeId == propertyOnlyType.Id).ToArray();
        Assert.NotEmpty(propertyOnlyAssignments);
        Assert.All(propertyOnlyAssignments, static assignment => Assert.Empty(assignment.ProductionContextAreaIds));
        Assert.NotEqual(packagesByFinding["widget"].Id, packagesByFinding["property-only"].Id);
        Assert.Contains(packagesByFinding["file-context"].DirectUncertainties, uncertainty => uncertainty.OriginTypeId is null
            && uncertainty.SourcePath == "tests/Product.Tests/TopLevel.cs");
        Assert.Equal(packagesByFinding["widget"].Id, packagesByFinding["single-test-file"].Id);
        Assert.Equal("tests/Product.Tests/Tests.cs", Assert.Single(packagesByFinding["many-test-file"].Areas).FilePath);
    }

    private static ReviewFinding CreateFinding(
        string analysisId,
        string projectPath,
        string sourcePath,
        string discriminator,
        string subjectId,
        (string Path, string Id, int Line, ProjectRole Role)[] subjects,
        bool isChanged = true)
    {
        var symbols = subjects.Select(subject => new FindingSymbol(subject.Path.StartsWith("tests/", StringComparison.Ordinal)
            ? "tests/Product.Tests/Product.Tests.csproj" : "src/Product.csproj", subject.Path, subject.Id, subject.Line)).ToArray();
        var draft = new FindingDraft(projectPath, sourcePath, subjectId, discriminator, subjects[0].Line, "Fixture finding.",
            new Dictionary<string, double>(), [new FindingEvidence(sourcePath, subjects[0].Line, "Fixture", "Fixture evidence", "public class")],
            symbols, symbols);
        var occurrences = subjects.Select(subject => new ReviewFindingOccurrence(
            new FindingSymbol(subject.Path.StartsWith("tests/", StringComparison.Ordinal)
                ? "tests/Product.Tests/Product.Tests.csproj" : "src/Product.csproj", subject.Path, subject.Id, subject.Line), subject.Role)).ToArray();
        return new ReviewFinding(analysisId, draft, [sourcePath], [], isChanged ? [sourcePath] : [])
        {
            Occurrences = occurrences,
            SubjectOccurrences = occurrences,
        };
    }

    private static string TypeId(AuditSourceContext context, string name) =>
        context.Types.Single(type => type.Name == name.Split('.').Last() && type.FullyQualifiedName == "global::" + name).Id.Split('|').Last();

    private static string MemberId(string id) => id;

    private static Fixture CreateFixture()
    {
#pragma warning disable CA2000
        var workspace = new AdhocWorkspace();
        var root = TestTempDirectory.Create();
#pragma warning restore CA2000
        var productionId = ProjectId.CreateNewId();
        var testId = ProjectId.CreateNewId();
        var productionPath = Path.Combine(root.DirectoryPath, "src", "Product.csproj");
        var testPath = Path.Combine(root.DirectoryPath, "tests", "Product.Tests", "Product.Tests.csproj");
        workspace.AddProject(CreateProject(productionId, "Product", productionPath));
        workspace.AddProject(CreateProject(testId, "Product.Tests", testPath));
        workspace.TryApplyChanges(workspace.CurrentSolution.AddProjectReference(testId, new ProjectReference(productionId)));
        AddDocument(workspace, productionId, root.DirectoryPath, "src/Product.cs", """
            namespace Product;
            public class Widget { public void Run() { RunInner(); } private void RunInner() { } public static int Value => 1; }
            public class WidgetExtra { public void Run() { } }
            public class Other { public void Run() { } }
            """);
        AddDocument(workspace, productionId, root.DirectoryPath, "src/Single.cs", "namespace Product; public class Single { public void Run() { } }");
        AddDocument(workspace, testId, root.DirectoryPath, "tests/Product.Tests/Tests.cs", """
            using static Product.Widget;
            namespace Product.Tests;
            public class WidgetTests { public void Run() { new Product.Widget().Run(); } }
            public class OtherTests { public void Run() { new Product.Other().Run(); } }
            public class UncertainTests { public void Run() { new Product.Widget().Run("wrong"); } }
            public partial class PartialTests { public void Run() { new Product.Widget().Run(); } }
            public partial class PartialSingleTests { public void Run() { new Product.Widget().Run(); } }
            public partial class PartialUncertainTests { public void Run() { new Product.Widget().Run(); } }
            public class NoReferenceTests { public void Run() { } }
            public class SharedHelper { public void Run() { new Product.Widget().Run(); new Product.Other().Run(); } }
            public class CallerWidget { public void Run() { new SharedHelper().Run(); } }
            public class CallerOther { public void Run() { new SharedHelper().Run(); } }
            public class PropertyOnlyTests { public int Read() => Value; }
            public class ContextTests { public void Run() { new Product.Widget().Run(); } }
            """);
        AddDocument(workspace, testId, root.DirectoryPath, "tests/Product.Tests/PartialB.cs", """
            namespace Product.Tests;
            public partial class PartialTests { public void Other() { new Product.Other().Run(); } }
            public partial class PartialSingleTests { public void Other() { new Product.Widget().Run(); } }
            public partial class PartialUncertainTests { public void Other() { new Product.Widget().Run("wrong"); } }
            """);
        AddDocument(workspace, testId, root.DirectoryPath, "tests/Product.Tests/TopLevel.cs", "Product.Widget.Run(\"wrong\");");
        AddDocument(workspace, testId, root.DirectoryPath, "tests/Product.Tests/SingleTest.cs",
            "namespace Product.Tests; public class SingleTest { public void Run() { new Product.Widget().Run(); } }");
        return new Fixture(workspace, root, new ReviewContext(workspace.CurrentSolution, root.DirectoryPath));
    }

    private static ProjectInfo CreateProject(ProjectId id, string name, string path) => ProjectInfo.Create(
        id, VersionStamp.Create(), name, name, LanguageNames.CSharp, filePath: path,
        compilationOptions: new CSharpCompilationOptions(name == "Product.Tests" ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary),
        parseOptions: new CSharpParseOptions(LanguageVersion.Preview), metadataReferences: FastTestReferences.CreatePlatformReferences());

    private static void AddDocument(AdhocWorkspace workspace, ProjectId projectId, string root, string relativePath, string source) =>
        workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(projectId), Path.GetFileName(relativePath),
            filePath: Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create()))));

    private sealed class Fixture(AdhocWorkspace workspace, TestTempDirectory root, ReviewContext context) : IDisposable
    {
        public ReviewContext Context { get; } = context;
        public void Dispose() { workspace.Dispose(); root.Dispose(); }
    }
}
