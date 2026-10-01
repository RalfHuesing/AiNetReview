namespace AiNetReview.FastTests;

using System.IO;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

public sealed class FastTestReferencesTests
{
    [Fact]
    public void FilterPlatformAssemblyPaths_ExcludesEveryRecognizedFrameworkFamilyCaseInsensitively()
    {
        string[] paths =
        [
            Path.Combine("runtime", "xUnit.Core.dll"),
            Path.Combine("runtime", "NUnit.Framework.dll"),
            Path.Combine("runtime", "MsTest.TestFramework.dll"),
            Path.Combine("runtime", "Microsoft.TestPlatform.ObjectModel.dll"),
            Path.Combine("runtime", "mIcRoSoFt.ViSuAlStUdIo.TeStPlAtFoRm.Common.dll"),
            Path.Combine("runtime", "MICROSOFT.VISUALSTUDIO.TESTTOOLS.UNITTESTING.dll"),
            Path.Combine("runtime", "Microsoft.Testing.Platform.dll"),
            Path.Combine("runtime", "System.Private.CoreLib.dll"),
            Path.Combine("runtime", "System.Runtime.dll"),
            Path.Combine("runtime", "MyCompany.Utility.dll"),
        ];

        var selected = FastTestReferences.FilterPlatformAssemblyPaths(paths);

        Assert.Equal(
            new[]
            {
                Path.Combine("runtime", "System.Private.CoreLib.dll"),
                Path.Combine("runtime", "System.Runtime.dll"),
                Path.Combine("runtime", "MyCompany.Utility.dll"),
            },
            selected);
    }

    [Fact]
    public void CreatePlatformReferences_LeavesAReferenceOnlyFixtureClassifiedAsProduction()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var project = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Example",
            "Example",
            LanguageNames.CSharp,
            filePath: Path.Combine(Path.GetTempPath(), "AiNetReview", "src", "Example.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: FastTestReferences.CreatePlatformReferences());
        workspace.AddProject(project);

        var classification = ReviewSourceClassifier.ClassifyProject(workspace.CurrentSolution.GetProject(projectId)!);

        Assert.Equal(ProjectRole.Production, classification.Role);
        Assert.Equal(ProjectClassificationReason.NoTestMarker, classification.Reason);
    }
}
