namespace AiNetReview.TestKit;

using System;
using System.IO;

/// <summary>
/// Sucht das Root-Verzeichnis der Projektmappe ausgehend vom aktuellen Anwendungsordner.
/// </summary>
public static class SolutionRootLocator
{
    private const string SolutionFileName = "AiNetReview.slnx";

    /// <summary>
    /// Ermittelt das Verzeichnis mit <c>AiNetReview.slnx</c>.
    /// </summary>
    public static string Find()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, SolutionFileName)))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException($"Das Root-Verzeichnis mit der Projektmappe '{SolutionFileName}' wurde nicht gefunden.");
    }
}
