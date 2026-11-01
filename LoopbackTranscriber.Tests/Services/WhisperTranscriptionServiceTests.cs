using System.Text.RegularExpressions;
using Whisper.net.Ggml;

namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class WhisperTranscriptionServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void ParseModelSize_IsCaseAndWhitespaceInsensitive()
    {
        Assert.Equal(GgmlType.LargeV3Turbo, WhisperTranscriptionService.ParseModelSize(" Turbo "));
    }

    [Fact]
    public void ParseModelSize_RejectsUnknownValuesInsteadOfFallingBackToSmall()
    {
        Assert.Throws<InvalidOperationException>(() => WhisperTranscriptionService.ParseModelSize("xyz"));
    }

    [Fact]
    public void ModelRevision_IsAFullCommitHash_NotAMutableRef()
    {
        Assert.Matches("^[0-9a-f]{40}$", WhisperTranscriptionService.ModelRevision);
    }

    [Fact]
    public void DefaultTurboModel_HasAPinnedSha256()
    {
        Assert.True(WhisperTranscriptionService.PinnedSha256.TryGetValue(GgmlType.LargeV3Turbo, out var hash));
        Assert.Matches("^[0-9a-f]{64}$", hash!);
    }

    [Fact]
    public void EveryPinnedHash_IsWellFormed()
    {
        foreach (var (type, hash) in WhisperTranscriptionService.PinnedSha256)
        {
            Assert.True(Regex.IsMatch(hash, "^[0-9a-f]{64}$"), $"Malformed hash for {type}");
        }
    }

    [Fact]
    public async Task ModelWithoutPinnedHash_ThrowsBeforeDownloadingAnything()
    {
        var destination = _temp.Combine("ggml-tiny.bin");
        var service = new WhisperTranscriptionService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureModelDownloadedAsync("tiny", destination));

        Assert.False(File.Exists(destination + ".download"));
        Assert.False(File.Exists(destination));
    }
}
