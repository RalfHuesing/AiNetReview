namespace AiNetReview.Core.Configuration;

using System;

public sealed class InvalidReviewInputException : Exception
{
    public InvalidReviewInputException(string message) : base(message)
    {
    }

    public InvalidReviewInputException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
