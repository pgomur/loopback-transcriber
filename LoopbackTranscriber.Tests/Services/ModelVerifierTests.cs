namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class ModelVerifierTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppSettings _settings;
    private readonly ModelVerifier _verifier;

    public ModelVerifierTests()
    {
        _settings = TestSettings.In(_temp.Path);
        _verifier = new ModelVerifier(new FakeSettingsStore(_settings));
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task WithoutModels_NothingIsRemoved()
    {
        var result = await _verifier.VerifyInstalledModelsAsync();

        Assert.False(result.AnyRemoved);
    }

    [Fact]
    public async Task CorruptModels_AreRemoved_SoTheAppAsksToDownloadThemAgain()
    {
        TestSettings.CreateModelFiles(_settings);

        var result = await _verifier.VerifyInstalledModelsAsync();

        Assert.True(result.TranscriptionModelRemoved);
        Assert.True(result.SummaryModelRemoved);
        Assert.True(result.AnyRemoved);
        Assert.False(File.Exists(_settings.GetModelFilePath()));
        Assert.False(File.Exists(_settings.GetSummaryModelFilePath()));
    }

    [Fact]
    public async Task OnlyTheCorruptModelIsReported()
    {
        TestSettings.CreateModelFiles(_settings);
        File.Delete(_settings.GetModelFilePath());

        var result = await _verifier.VerifyInstalledModelsAsync();

        Assert.False(result.TranscriptionModelRemoved);
        Assert.True(result.SummaryModelRemoved);
    }

    [Fact]
    public async Task VerifyInstalledModelsAsync_CanBeCancelled()
    {
        TestSettings.CreateModelFiles(_settings);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _verifier.VerifyInstalledModelsAsync(cancellation.Token));
        Assert.True(File.Exists(_settings.GetModelFilePath()));
    }
}
