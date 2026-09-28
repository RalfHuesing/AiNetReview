namespace AiNetReview.Core.Analysis;

/// <summary>Markup content captured while the solution is loaded.</summary>
public sealed record MarkupDocumentSnapshot(string FilePath, string Text);
