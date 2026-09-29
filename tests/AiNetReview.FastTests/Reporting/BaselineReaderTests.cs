namespace AiNetReview.FastTests.Reporting;

using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Reporting;

public sealed class BaselineReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsNullWhenBaselineIsMissing()
    {
        using var temp = TestTempDirectory.Create();

        var result = await new BaselineReader().ReadAsync(temp.DirectoryPath);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadAsync_ReturnsValidatedProjectRelativeHashes()
    {
        using var temp = TestTempDirectory.Create();
        var expected = new SourceBaselineDocument(1, [new SourceFileSnapshot("src/A.cs", new string('a', 64))]);
        await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, "baseline.json"), JsonSerializer.Serialize(expected, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));

        var result = await new BaselineReader().ReadAsync(temp.DirectoryPath);

        Assert.NotNull(result);
        Assert.Equal(new string('a', 64), Assert.Single(result).Value);
        Assert.Equal("src/A.cs", Assert.Single(result).Key);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":1,\"Files\":[{\"Path\":\"../outside.cs\",\"Sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}]}")]
    [InlineData("{\"SchemaVersion\":1,\"Files\":[{\"Path\":\"src/A.cs\",\"Sha256\":\"invalid\"}]}")]
    public async Task ReadAsync_RejectsInvalidBaselineDocuments(string json)
    {
        using var temp = TestTempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, "baseline.json"), json);

        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineReader().ReadAsync(temp.DirectoryPath));
    }
}
