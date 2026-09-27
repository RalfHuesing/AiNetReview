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

    [Fact]
    public void GetPath_WithTraversalOrRootedPath_ThrowsArgumentException()
    {
        using var temp = TestTempDirectory.Create();

        Assert.Throws<ArgumentNullException>(() => temp.GetPath(null!));
        Assert.Throws<ArgumentException>(() => temp.GetPath(""));
        Assert.Throws<ArgumentException>(() => temp.GetPath("   "));
        Assert.Throws<ArgumentException>(() => temp.GetPath("../outside.txt"));
        Assert.Throws<ArgumentException>(() => temp.GetPath(@"sub\..\..\outside.txt"));
        Assert.Throws<ArgumentException>(() => temp.GetPath("sub/../../outside.txt"));

        var rootedPath = OperatingSystem.IsWindows() ? @"C:\Windows\System32" : "/etc/passwd";
        Assert.Throws<ArgumentException>(() => temp.GetPath(rootedPath));
    }

    [Fact]
    public void CreateFile_WithTraversalPath_ThrowsArgumentException()
    {
        using var temp = TestTempDirectory.Create();

        Assert.Throws<ArgumentException>(() => temp.CreateFile("../outside.txt", "evil"));
        Assert.Throws<ArgumentException>(() => temp.CreateSubdirectory("../outside_sub"));
    }

    [Fact]
    public void Create_WithInvalidPrefix_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => TestTempDirectory.Create(null!));
        Assert.Throws<ArgumentException>(() => TestTempDirectory.Create(""));
        Assert.Throws<ArgumentException>(() => TestTempDirectory.Create("   "));
        Assert.Throws<ArgumentException>(() => TestTempDirectory.Create("../evil"));
        Assert.Throws<ArgumentException>(() => TestTempDirectory.Create(@"evil\sub"));
        Assert.Throws<ArgumentException>(() => TestTempDirectory.Create("custom-without-ainet-prefix-"));
    }

    [Fact]
    public void Cleanup_PreservesForeignDirectoriesEvenWithGuidSuffix()
    {
        var root = TestTempDirectory.RootTempDirectory;
        Directory.CreateDirectory(root);

        // A foreign directory ending with a 32-char GUID N-format, but not starting with ainet-
        var foreignDirName = $"foreign-{Guid.NewGuid():N}";
        var foreignDirPath = Path.Combine(root, foreignDirName);
        Directory.CreateDirectory(foreignDirPath);

        try
        {
            // Set write time older than stale age (5 minutes)
            Directory.SetLastWriteTimeUtc(foreignDirPath, DateTime.UtcNow.AddMinutes(-10));

            // Triggering cleanup via Create()
            using (var temp = TestTempDirectory.Create())
            {
                Assert.NotNull(temp);
            }

            // Foreign directory must NOT be deleted
            Assert.True(Directory.Exists(foreignDirPath), "Foreign directory should not be deleted by cleanup.");
        }
        finally
        {
            if (Directory.Exists(foreignDirPath))
            {
                Directory.Delete(foreignDirPath, recursive: true);
            }
        }
    }
}
