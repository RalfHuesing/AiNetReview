namespace AiNetReview.Core.Analysis;

/// <summary>A project-relative source file and the SHA-256 of its current bytes.</summary>
public sealed record SourceFileSnapshot(string Path, string Sha256);
