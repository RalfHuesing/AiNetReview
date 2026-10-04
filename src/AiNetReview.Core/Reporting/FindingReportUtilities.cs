namespace AiNetReview.Core.Reporting;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AiNetReview.Core.Analysis;

/// <summary>Stable finding identity and route values shared by report writers.</summary>
internal static class FindingReportUtilities
{
    internal static string GetFindingArea(ReviewFinding finding)
    {
        var roles = finding.SubjectOccurrences.Select(static occurrence => occurrence.Role).Distinct().ToArray();
        if (roles.Contains(ProjectRole.Production) && roles.Contains(ProjectRole.Tests))
        {
            return "mixed";
        }

        return roles.Contains(ProjectRole.Tests) ? "tests" : "production";
    }

    internal static string FindingKey(ReviewFinding finding) => finding.AnalysisId + "\0" + finding.Finding.ProjectPath + "\0"
        + finding.Finding.SourcePath + "\0" + finding.Finding.SubjectId + "\0" + finding.Finding.Discriminator;

    internal static string GetFindingId(ReviewFinding finding)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(FindingKey(finding)));
        return "finding-" + Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
    }

    internal static string EncodePathSegment(string segment) => Uri.EscapeDataString(segment);
}
