namespace AiNetReview.FastTests.Findings;

using System;
using AiNetReview.Core.Findings;

public sealed class FingerprintServiceTests
{
    [Fact]
    public void Compute_UsesVersionLengthAndUtf8TextAsBigEndianBytes()
    {
        var actual = new FingerprintService().Compute(1, "é");

        Assert.Equal("sha256:8e259783c524b2ef1a0308ad630824a484873c044fc4af702ac3cbf7f1f51e71", actual);
        Assert.Matches("^sha256:[0-9a-f]{64}$", actual);
    }

    [Fact]
    public void Compute_RejectsNonpositiveVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FingerprintService().Compute(0, "text"));
    }
}
