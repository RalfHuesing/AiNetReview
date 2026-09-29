namespace AiNetReview.Core.Analysis;

using System.Collections.Generic;

/// <summary>The complete persisted source snapshot used to compare review runs.</summary>
public sealed record SourceBaselineDocument(int SchemaVersion, IReadOnlyList<SourceFileSnapshot> Files);
