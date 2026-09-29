namespace AiNetReview.Core.Reporting;

using System;

/// <summary>Identifies the actual host invocation needed to create a baseline for a centrally hosted audit.</summary>
public sealed record BaselineCommandContext(string ExecutablePath, string ConfigurationPath, string OutputDirectory)
{
    public string ExecutablePath { get; } = RequireAbsolute(ExecutablePath, nameof(ExecutablePath));

    public string ConfigurationPath { get; } = RequireAbsolute(ConfigurationPath, nameof(ConfigurationPath));

    public string OutputDirectory { get; } = RequireAbsolute(OutputDirectory, nameof(OutputDirectory));

    private static string RequireAbsolute(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (!System.IO.Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The baseline command path must be absolute.", parameterName);
        }

        return System.IO.Path.GetFullPath(path);
    }
}
