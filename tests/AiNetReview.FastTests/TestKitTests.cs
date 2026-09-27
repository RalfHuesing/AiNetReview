namespace AiNetReview.FastTests;

using System;
using System.Threading.Tasks;

public sealed class TestKitTests
{
    [Fact]
    public async Task TestWaiter_SucceedsWhenConditionBecomesTrue()
    {
        var count = 0;
        await TestWaiter.WaitForConditionAsync(() => ++count >= 3, TimeSpan.FromSeconds(2));
        Assert.True(count >= 3);
    }
}
