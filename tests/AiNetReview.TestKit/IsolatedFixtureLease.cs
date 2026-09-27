namespace AiNetReview.TestKit;

using System;
using System.IO;
using System.Linq;

/// <summary>
/// Isolierte Temp-Kopie einer kanonischen Mini-Solution aus <c>tests/AiNetReview.IntegrationTests/Fixtures/&lt;fixtureFolderName&gt;/</c>.
/// </summary>
public sealed class IsolatedFixtureLease : IDisposable
{
    private readonly TestTempDirectory tempDir;

    private IsolatedFixtureLease(TestTempDirectory tempDir)
    {
        this.tempDir = tempDir;
        RootPath = tempDir.DirectoryPath;
    }

    /// <summary>
    /// Wurzelverzeichnis der isolierten Kopie.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Kopiert <c>tests/AiNetReview.IntegrationTests/Fixtures/&lt;fixtureFolderName&gt;/</c> unterhalb von <paramref name="solutionRoot"/>
    /// unter Auslassung von <c>bin</c>/<c>obj</c>-Unterordnern in ein neues, eindeutiges Temp-Verzeichnis.
    /// </summary>
    public static IsolatedFixtureLease CopyFixture(
        string solutionRoot, string fixtureFolderName, string tempPrefix = "ainet-fixture-")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureFolderName);
        if (fixtureFolderName.Contains('/') || fixtureFolderName.Contains('\\') || fixtureFolderName.Contains(".."))
        {
            throw new ArgumentException("Fixture-Ordnername darf keine Pfadtrennzeichen oder '..'-Segmente enthalten.", nameof(fixtureFolderName));
        }

        var sourceRoot = Path.Combine(solutionRoot, "tests", "AiNetReview.IntegrationTests", "Fixtures", fixtureFolderName);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"Fixture-Verzeichnis nicht gefunden: {sourceRoot}");
        }

        var tempDirectory = TestTempDirectory.Create(tempPrefix);

        try
        {
            CopyDirectory(sourceRoot, tempDirectory.DirectoryPath);
            return new IsolatedFixtureLease(tempDirectory);
        }
        catch
        {
            tempDirectory.Dispose();
            throw;
        }
    }

    public void Dispose() => tempDir.Dispose();

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
            if (IsGeneratedPath(relativePath))
            {
                continue;
            }

            var targetFile = Path.Combine(destinationRoot, relativePath);
            var dir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.Copy(sourceFile, targetFile, overwrite: true);
        }
    }

    private static bool IsGeneratedPath(string relativePath)
    {
        var parts = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Contains("obj", StringComparer.OrdinalIgnoreCase) ||
               parts.Contains("bin", StringComparer.OrdinalIgnoreCase);
    }
}
