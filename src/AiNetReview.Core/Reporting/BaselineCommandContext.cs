namespace AiNetReview.Core.Reporting;

using System;

/// <summary>Identifies the manual audit script invocation needed to update a centrally stored baseline.</summary>
public sealed record BaselineCommandContext(string ScriptPath, string TargetName)
{
    public string ScriptPath { get; } = RequireAbsolute(ScriptPath, nameof(ScriptPath));

    public string TargetName { get; } = RequireTargetName(TargetName);

    private static string RequireAbsolute(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (!System.IO.Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The baseline command path must be absolute.", parameterName);
        }

        return System.IO.Path.GetFullPath(path);
    }

    private static string RequireTargetName(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (targetName.Length > 64
            || (!char.IsAsciiLetterLower(targetName[0]) && !char.IsAsciiDigit(targetName[0])))
        {
            throw new ArgumentException("The audit target name is invalid.", nameof(targetName));
        }

        for (var index = 1; index < targetName.Length; index++)
        {
            var character = targetName[index];
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                throw new ArgumentException("The audit target name is invalid.", nameof(targetName));
            }
        }

        return targetName;
    }
}
