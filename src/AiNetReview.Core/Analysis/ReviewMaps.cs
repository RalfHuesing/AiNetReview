namespace AiNetReview.Core.Analysis;

using System.Collections.Generic;

/// <summary>Snapshot-derived, report-ready source maps for one loaded solution.</summary>
public sealed record ReviewMaps(
    IReadOnlyList<ReviewMapProject> Projects,
    IReadOnlyList<ReviewMapSourceFile> Files,
    IReadOnlyList<ReviewMapType> Types,
    IReadOnlyList<ReviewMapTypeEdge> TypeEdges);

/// <summary>A loaded C# project and its direct loaded C# project references.</summary>
public sealed record ReviewMapProject(
    string Key,
    string ProjectPath,
    ProjectRole Role,
    ProjectClassificationReason ClassificationReason,
    IReadOnlyList<ReviewMapProjectReference> References);

/// <summary>A direct project reference whose target is also loaded as C#.</summary>
public sealed record ReviewMapProjectReference(
    string TargetProjectKey,
    string TargetProjectPath,
    ProjectRole TargetRole);

/// <summary>A non-generated C# document as captured in the loaded solution snapshot.</summary>
public sealed record ReviewMapSourceFile(
    string ProjectKey,
    string RelativePath,
    long Utf8Bytes,
    int Lines,
    IReadOnlyList<string> Namespaces,
    IReadOnlyList<string> TypeIds);

/// <summary>A source type and every loaded declaration location, including partial declarations.</summary>
public sealed record ReviewMapType(
    string Id,
    string ProjectKey,
    string Namespace,
    string Name,
    string Kind,
    string FullyQualifiedName,
    IReadOnlyList<ReviewMapTypeDeclaration> Declarations);

/// <summary>A declaration's project-relative source path and one-based line.</summary>
public sealed record ReviewMapTypeDeclaration(string SourcePath, int Line);

/// <summary>A direct statically bound type dependency retained by the review type graph.</summary>
public sealed record ReviewMapTypeEdge(
    string FromTypeId,
    string ToTypeId,
    bool IsTestContext,
    IReadOnlyList<ReviewMapTypeWitness> Witnesses);

/// <summary>The retained source witness for one dependency evidence kind.</summary>
public sealed record ReviewMapTypeWitness(
    string ProjectKey,
    string SourcePath,
    int Line,
    string Kind);
