namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class FileCleanupTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void TryDelete_RemovesTheFile()
    {
        var path = _temp.WriteText("a.txt", "x");

        FileCleanup.TryDelete(path, "test");

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryDelete_IgnoresAnEmptyPath(string? path)
    {
        FileCleanup.TryDelete(path, "test");
        FileCleanup.TryDeleteFolder(path, "test");
    }

    [Fact]
    public void TryDelete_IgnoresAMissingFile()
    {
        FileCleanup.TryDelete(_temp.Combine("missing.txt"), "test");
        FileCleanup.TryDeleteFolder(_temp.Combine("missing"), "test");
    }

    [Fact]
    public void TryDelete_DoesNotThrowWhenTheFileIsInUse_AndLeavesItInPlace()
    {
        var path = _temp.WriteText("busy.txt", "x");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            FileCleanup.TryDelete(path, "test");
        }

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void TryDeleteFolder_RemovesEverythingInside()
    {
        var folder = _temp.CreateFolder("work");
        _temp.WriteText(Path.Combine("work", "nested", "file.txt"), "x");

        FileCleanup.TryDeleteFolder(folder, "test");

        Assert.False(Directory.Exists(folder));
    }
}
