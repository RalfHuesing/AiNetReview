namespace AiNetReview.Core.Findings;

using System;

public sealed record FindingIdentity(
    string RuleId,
    string ProjectPath,
    string SourcePath,
    string SubjectId,
    string Discriminator)
{
    public static FindingIdentity FromDraft(string ruleId, FindingDraft draft)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentNullException.ThrowIfNull(draft);
        return new FindingIdentity(ruleId, draft.ProjectPath, draft.SourcePath, draft.SubjectId, draft.Discriminator);
    }
}
