namespace AiNetReview.Core.Analysis;

/// <summary>Harmless declarations retained to inspect dead-code audit reporting.</summary>
internal sealed class deadcode_test_UnusedClass
{
    internal void deadcode_test_GroupedMember()
    {
    }
}

/// <summary>Provides one unused method on a type referenced by the test project.</summary>
internal sealed class deadcode_test_MethodHost
{
    internal static void Touch()
    {
    }

    private void deadcode_test_UnusedMethod()
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
