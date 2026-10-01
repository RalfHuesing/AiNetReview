namespace AiNetReview.Core.Analysis;

/// <summary>The existing project-level source role used for review attribution.</summary>
public enum ProjectRole
{
    Production,
    Tests,
}

/// <summary>The existing marker that led the source classifier to a project role.</summary>
public enum ProjectClassificationReason
{
    TestReferenceAssembly,
    ProjectNameSuffix,
    ProjectFileNameSuffix,
    ProjectPathSegment,
    NoTestMarker,
}

/// <summary>The source classifier's role and reason for one loaded C# project.</summary>
public sealed record ProjectClassification(string ProjectPath, ProjectRole Role, ProjectClassificationReason Reason);

internal sealed record ProjectClassificationResult(ProjectRole Role, ProjectClassificationReason Reason);
