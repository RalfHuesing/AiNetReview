namespace AiNetReview.Core.Analysis;

using System;

public sealed class AnalysisFailedException : Exception
{
    public AnalysisFailedException(string message) : base(message)
    {
    }

    public AnalysisFailedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
