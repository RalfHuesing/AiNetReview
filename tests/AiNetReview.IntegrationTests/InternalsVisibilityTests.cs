namespace AiNetReview.IntegrationTests;

using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AiNetReview.Bootstrap;
using Xunit;

public sealed class InternalsVisibilityTests
{
    [Fact]
    public void HostAssembly_GrantsInternalsVisibleTo_IntegrationTestsAndFastTests()
    {
        var attributes = typeof(ServiceRegistration).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attr => attr.AssemblyName)
            .ToList();

        Assert.Contains("AiNetReview.IntegrationTests", attributes);
        Assert.Contains("AiNetReview.FastTests", attributes);
    }
}
