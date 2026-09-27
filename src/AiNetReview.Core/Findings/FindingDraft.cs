namespace AiNetReview.Core.Findings;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

public sealed class FindingDraft
{
    public FindingDraft(
        string projectPath,
        string sourcePath,
        string subjectId,
        string discriminator,
        int startLine,
        string rationale,
        IReadOnlyDictionary<string, double> metrics,
        IEnumerable<FindingEvidence> evidence,
        string snapshot,
        string comparisonText,
        int fingerprintVersion,
        IEnumerable<string> sourceFiles)
    {
        ProjectPath = projectPath;
        SourcePath = sourcePath;
        SubjectId = subjectId;
        Discriminator = discriminator;
        StartLine = startLine;
        Rationale = rationale;
        Metrics = new ReadOnlyDictionary<string, double>(new SortedDictionary<string, double>(metrics.ToDictionary(static pair => pair.Key, static pair => pair.Value), System.StringComparer.Ordinal));
        Evidence = Array.AsReadOnly(evidence.ToArray());
        Snapshot = snapshot;
        ComparisonText = comparisonText;
        FingerprintVersion = fingerprintVersion;
        SourceFiles = Array.AsReadOnly(sourceFiles.ToArray());
    }

    public string ProjectPath { get; }

    public string SourcePath { get; }

    public string SubjectId { get; }

    public string Discriminator { get; }

    public int StartLine { get; }

    public string Rationale { get; }

    public IReadOnlyDictionary<string, double> Metrics { get; }

    public IReadOnlyList<FindingEvidence> Evidence { get; }

    public string Snapshot { get; }

    public string ComparisonText { get; }

    public int FingerprintVersion { get; }

    public IReadOnlyList<string> SourceFiles { get; }
}

public sealed record FindingEvidence(string SourcePath, int Line, string Label, string Detail, string Snippet);
