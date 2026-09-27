namespace AiNetReview.FastTests;

using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AiNetReview.Core.Analysis;
using AiNetReview.TestKit;
using Xunit;

public sealed class InternalsVisibilityTests
{
    [Fact]
    public void CoreAssembly_GrantsInternalsVisibleTo_FastTestsAndIntegrationTests()
    {
        var attributes = typeof(ReviewRunner).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attr => attr.AssemblyName)
            .ToList();

        Assert.Contains("AiNetReview.FastTests", attributes);
        Assert.Contains("AiNetReview.IntegrationTests", attributes);
    }

    [Fact]
    public void TestKitAssembly_GrantsInternalsVisibleTo_FastTestsAndIntegrationTests()
    {
        var attributes = typeof(TestTempDirectory).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attr => attr.AssemblyName)
            .ToList();

        Assert.Contains("AiNetReview.FastTests", attributes);
        Assert.Contains("AiNetReview.IntegrationTests", attributes);
    }
}
