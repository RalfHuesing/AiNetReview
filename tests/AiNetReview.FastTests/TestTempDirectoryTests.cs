namespace AiNetReview.FastTests;

using System.IO;

public sealed class TestTempDirectoryTests
{
    [Fact]
    public void TestTempDirectory_CreatesAndCleansUpDirectoryUnderTemp()
    {
        string directoryPath;
        string filePath;

        using (var temp = TestTempDirectory.Create())
        {
            directoryPath = temp.DirectoryPath;
            Assert.True(Directory.Exists(directoryPath));
            Assert.StartsWith(TestTempDirectory.RootTempDirectory, directoryPath);

            filePath = temp.CreateFile("sample.txt", "test content");
            Assert.True(File.Exists(filePath));
            Assert.Equal("test content", File.ReadAllText(filePath));
        }

        Assert.False(Directory.Exists(directoryPath));
        Assert.False(File.Exists(filePath));
    }
}
