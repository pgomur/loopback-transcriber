using System.Security.Cryptography;
using System.Text;

namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class ModelIntegrityTests : IDisposable
{
    private const string MarkerSuffix = ".verified.json";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task AMissingModel_IsNotVerified()
    {
        Assert.False(await ModelIntegrity.EnsureVerifiedAsync(_temp.Combine("model.bin"), Sha256Of("x"), "modelo", null, default));
    }

    [Fact]
    public async Task AnIntactModel_IsVerified_AndAMarkerIsWritten()
    {
        var model = Model("contenido del modelo");
        var progress = new CollectingProgress<UserMessage>();

        Assert.True(await ModelIntegrity.EnsureVerifiedAsync(model, Sha256Of("contenido del modelo"), "modelo", progress, default));

        Assert.True(File.Exists(model + MarkerSuffix));
        Assert.Contains(progress.Reports, m => m.Key == "ProgressVerifyingModel");
    }

    [Fact]
    public async Task TheHashIsComparedIgnoringCase()
    {
        var model = Model("abc");

        Assert.True(await ModelIntegrity.EnsureVerifiedAsync(model, Sha256Of("abc").ToLowerInvariant(), "modelo", null, default));
    }

    [Fact]
    public async Task WithAValidMarker_TheModelIsNotHashedAgain()
    {
        var model = Model("contenido original");
        var hash = Sha256Of("contenido original");
        await ModelIntegrity.EnsureVerifiedAsync(model, hash, "modelo", null, default);
        var writtenAt = File.GetLastWriteTimeUtc(model);

        File.WriteAllText(model, "contenido distinto");
        File.SetLastWriteTimeUtc(model, writtenAt);
        var progress = new CollectingProgress<UserMessage>();

        Assert.True(await ModelIntegrity.EnsureVerifiedAsync(model, hash, "modelo", progress, default));
        Assert.Empty(progress.Reports);
    }

    [Fact]
    public async Task AModelThatChangedOnDisk_IsHashedAgain_AndDeletedWhenCorrupt()
    {
        var model = Model("contenido original");
        var hash = Sha256Of("contenido original");
        await ModelIntegrity.EnsureVerifiedAsync(model, hash, "modelo", null, default);

        File.WriteAllText(model, "contenido corrompido y mas largo");

        Assert.False(await ModelIntegrity.EnsureVerifiedAsync(model, hash, "modelo", null, default));
        Assert.False(File.Exists(model));
        Assert.False(File.Exists(model + MarkerSuffix));
    }

    [Fact]
    public async Task AModelWithTheWrongHash_IsDeletedSoItCanBeDownloadedAgain()
    {
        var model = Model("modelo corrupto");

        Assert.False(await ModelIntegrity.EnsureVerifiedAsync(model, Sha256Of("otro contenido"), "modelo", null, default));
        Assert.False(File.Exists(model));
    }

    [Fact]
    public async Task AMarkerForAnotherPinnedHash_IsNotTrusted()
    {
        var model = Model("version 1");
        await ModelIntegrity.EnsureVerifiedAsync(model, Sha256Of("version 1"), "modelo", null, default);

        Assert.False(await ModelIntegrity.EnsureVerifiedAsync(model, Sha256Of("version 2"), "modelo", null, default));
    }

    [Fact]
    public async Task AnUnreadableMarker_IsIgnored_AndTheModelIsHashedAgain()
    {
        var model = Model("contenido");
        File.WriteAllText(model + MarkerSuffix, "{ roto");

        Assert.True(await ModelIntegrity.EnsureVerifiedAsync(model, Sha256Of("contenido"), "modelo", null, default));
    }

    [Fact]
    public async Task ConcurrentChecksOfTheSameModel_BothSucceed()
    {
        var model = Model(new string('m', 200_000));
        var hash = Sha256Of(new string('m', 200_000));

        var results = await Task.WhenAll(
            ModelIntegrity.EnsureVerifiedAsync(model, hash, "modelo", null, default),
            ModelIntegrity.EnsureVerifiedAsync(model, hash, "modelo", null, default));

        Assert.All(results, Assert.True);
    }

    [Fact]
    public async Task VerifySha256Async_ThrowsATranslatableErrorOnMismatch()
    {
        var model = Model("contenido");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            ModelIntegrity.VerifySha256Async(model, Sha256Of("otro"), "Whisper", null, default));

        Assert.True(UserError.TryGetKey(error, out var key, out var args));
        Assert.Equal("ErrorModelHashMismatch", key);
        Assert.Equal(new object?[] { "Whisper" }, args);
    }

    [Fact]
    public async Task InvalidateMarker_ForcesTheNextCheckToHashTheModel()
    {
        var model = Model("contenido");
        await ModelIntegrity.EnsureVerifiedAsync(model, Sha256Of("contenido"), "modelo", null, default);

        ModelIntegrity.InvalidateMarker(model);

        Assert.False(File.Exists(model + MarkerSuffix));
    }

    [Fact]
    public async Task LoadFailed_ForgetsTheVerification_AndExplainsWhichFileFailed()
    {
        var model = Model("contenido");
        await ModelIntegrity.EnsureVerifiedAsync(model, Sha256Of("contenido"), "modelo", null, default);
        var loadError = new InvalidOperationException("gguf truncated");

        var error = ModelIntegrity.LoadFailed(loadError, model);

        Assert.IsType<InvalidDataException>(error);
        Assert.Same(loadError, error.InnerException);
        Assert.True(UserError.TryGetKey(error, out var key, out var args));
        Assert.Equal("ErrorModelLoadFailed", key);
        Assert.Equal(new object?[] { "model.bin" }, args);
        Assert.False(File.Exists(model + MarkerSuffix));
        Assert.True(File.Exists(model));
    }

    private string Model(string content) => _temp.WriteText("model.bin", content);

    private static string Sha256Of(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
