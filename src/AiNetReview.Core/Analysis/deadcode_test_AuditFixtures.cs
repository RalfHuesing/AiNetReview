namespace AiNetReview.Core.Analysis;

/// <summary>Provides one unused method on a type referenced by the test project.</summary>
internal sealed class deadcode_test_MethodHost
{
    internal static void Touch()
    {
    }
}

/// <summary>Used only by a unit test, so the audit should protect both declarations.</summary>
internal sealed class deadcode_test_UsedOnlyByUnitTest
{
    internal static void deadcode_test_ReferencedByTest()
    {
    }
}
