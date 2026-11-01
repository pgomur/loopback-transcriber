using System.Text.Json;

namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class OrphanRecordingRecoveryTests : IDisposable
{
    private const string BaseName = "recording_20261018_093000";
    private static readonly DateTime StartedUtc = new(2026, 10, 18, 7, 30, 0, DateTimeKind.Utc);
    private static readonly UserMessage Reason = UserMessage.FromKey("RecordingRecoveredReason");

    private readonly TempDirectory _temp = new();
    private readonly string _tempFolder;
    private readonly FailedRecordingStore _store;

    public OrphanRecordingRecoveryTests()
    {
        _tempFolder = _temp.CreateFolder("tmp");
        _store = new FailedRecordingStore(_temp.Combine("failed"));
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void WithoutTempFolder_NothingIsRecovered()
    {
        var result = Run(tempFolder: _temp.Combine("does-not-exist"));

        Assert.Empty(result.Recovered);
        Assert.Empty(result.Failed);
        Assert.Equal(0, result.Deferred);
    }

    [Fact]
    public void AnInterruptedRecording_IsMovedToTheFailedRecordings_WithItsSessionDetails()
    {
        WriteSession(BaseName, processStarted: StartedUtc, micStarted: StartedUtc.AddMilliseconds(400));
        Track(BaseName, seconds: 1.0, amplitude: 0.5);
        Track(BaseName + "_mic", seconds: 1.0, amplitude: 0.5);

        var result = Run();

        var recovered = Assert.Single(result.Recovered);
        Assert.Equal("Teams", recovered.AppName);
        Assert.Equal(new DateTime(2026, 10, 18, 9, 30, 0), recovered.RecordedAt);
        Assert.Equal(TimeSpan.FromMilliseconds(400), recovered.MicStartOffset);
        Assert.NotNull(recovered.MicWavPath);
        Assert.Equal(Reason.Key, recovered.ErrorKey);
        Assert.Empty(Directory.GetFiles(_tempFolder));
        Assert.Single(_store.List());
    }

    [Fact]
    public void WhenTheAppTrackIsSilent_TheLoudestDeviceTrackIsUsed_AndTheOffsetIsMeasuredAgainstIt()
    {
        WriteSession(BaseName, processStarted: StartedUtc, deviceStarted: StartedUtc.AddMilliseconds(100),
            micStarted: StartedUtc.AddMilliseconds(400));
        Track(BaseName, seconds: 1.0, amplitude: 0);
        Track(BaseName + "_speakers", seconds: 2.0, amplitude: 0.2);
        Track(BaseName + "_headset", seconds: 3.0, amplitude: 0.6);
        Track(BaseName + "_mic", seconds: 1.0, amplitude: 0.5);

        var recovered = Assert.Single(Run().Recovered);

        Assert.InRange(WavFile.Duration(recovered.SourceWavPath).TotalSeconds, 2.99, 3.01);
        Assert.Equal(TimeSpan.FromMilliseconds(300), recovered.MicStartOffset);
        Assert.Empty(Directory.GetFiles(_tempFolder));
    }

    [Fact]
    public void WhenTheAppTrackHasSignal_DeviceTracksAreIgnored()
    {
        WriteSession(BaseName);
        Track(BaseName, seconds: 1.0, amplitude: 0.1);
        Track(BaseName + "_speakers", seconds: 2.0, amplitude: 0.9);

        var recovered = Assert.Single(Run().Recovered);

        Assert.InRange(WavFile.Duration(recovered.SourceWavPath).TotalSeconds, 0.99, 1.01);
    }

    [Fact]
    public void WithOnlyTheMicrophone_TheMicrophoneBecomesTheSource()
    {
        WriteSession(BaseName);
        Track(BaseName + "_mic", seconds: 1.0, amplitude: 0.5);

        var recovered = Assert.Single(Run().Recovered);

        Assert.Null(recovered.MicWavPath);
        Assert.True(File.Exists(recovered.SourceWavPath));
    }

    [Fact]
    public void ARecordingWithoutAnyAudio_IsDiscarded()
    {
        WriteSession(BaseName);
        WavFile.WriteSilence(Path.Combine(_tempFolder, BaseName + ".wav"), 0);
        WavFile.WriteSilence(Path.Combine(_tempFolder, BaseName + "_mic.wav"), 0);

        var result = Run();

        Assert.Empty(result.Recovered);
        Assert.Empty(result.Failed);
        Assert.Empty(Directory.GetFiles(_tempFolder));
        Assert.Empty(_store.List());
    }

    [Fact]
    public void TheRecordingInProgress_IsLeftAlone()
    {
        WriteSession(BaseName);
        Track(BaseName, seconds: 1.0, amplitude: 0.5);

        var result = Run(isOwned: name => name == BaseName);

        Assert.Empty(result.Recovered);
        Assert.Equal(2, Directory.GetFiles(_tempFolder).Length);
    }

    [Fact]
    public void AFileStillInUse_IsDeferred_AndRecoveredLater()
    {
        WriteSession(BaseName);
        var track = Track(BaseName, seconds: 1.0, amplitude: 0.5);

        using (new FileStream(track, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var busy = Run();

            Assert.Equal(1, busy.Deferred);
            Assert.Empty(busy.Recovered);
            Assert.True(File.Exists(track));
        }

        Assert.Single(Run().Recovered);
    }

    [Fact]
    public void TracksWithoutSessionFile_AreRecoveredWithTheDateFromTheirName()
    {
        Track(BaseName, seconds: 1.0, amplitude: 0.5);
        Track(BaseName + "_mic", seconds: 1.0, amplitude: 0.5);

        var recovered = Assert.Single(Run().Recovered);

        Assert.Equal(RecordingNames.DefaultAppName, recovered.AppName);
        Assert.Equal(new DateTime(2026, 10, 18, 9, 30, 0), recovered.RecordedAt);
        Assert.NotNull(recovered.MicWavPath);
        Assert.Empty(Directory.GetFiles(_tempFolder));
    }

    [Fact]
    public void RecordingsWithTheLegacyPrefix_AreRecoveredToo()
    {
        const string legacy = "grabacion_20250101_080000";
        WriteSession(legacy);
        Track(legacy, seconds: 1.0, amplitude: 0.5);

        Assert.Single(Run().Recovered);
        Assert.Empty(Directory.GetFiles(_tempFolder));
    }

    [Fact]
    public void ACorruptSessionFile_IsReportedAsFailed_AndTheAudioIsKept()
    {
        File.WriteAllText(Path.Combine(_tempFolder, BaseName + OrphanRecordingRecovery.SessionFileSuffix), "{ roto");
        var track = Track(BaseName, seconds: 1.0, amplitude: 0.5);

        var result = Run();

        Assert.Empty(result.Recovered);
        Assert.Equal(Path.Combine(_tempFolder, BaseName), Assert.Single(result.Failed));
        Assert.True(File.Exists(track));
    }

    [Fact]
    public void ARecordingCutShortByACrash_IsRepairedBeforeBeingSaved()
    {
        WriteSession(BaseName);
        var track = Track(BaseName, seconds: 1.0, amplitude: 0.5);
        WavFile.BreakHeaderSizes(track);

        var recovered = Assert.Single(Run().Recovered);

        Assert.InRange(WavFile.Duration(recovered.SourceWavPath).TotalSeconds, 0.99, 1.01);
    }

    [Fact]
    public void LeftoverConvertedFiles_AreDeletedWithTheRecording()
    {
        WriteSession(BaseName);
        Track(BaseName, seconds: 1.0, amplitude: 0.5);
        Track(BaseName + OrphanRecordingRecovery.DerivedSuffix[..^4], seconds: 1.0, amplitude: 0.5);

        Assert.Single(Run().Recovered);
        Assert.Empty(Directory.GetFiles(_tempFolder));
    }

    [Fact]
    public void SeveralOrphans_AreAllRecovered()
    {
        WriteSession(BaseName);
        Track(BaseName, seconds: 1.0, amplitude: 0.5);
        WriteSession("recording_20261019_100000");
        Track("recording_20261019_100000", seconds: 1.0, amplitude: 0.5);

        Assert.Equal(2, Run().Recovered.Count);
        Assert.Equal(2, _store.List().Count);
    }

    private OrphanRecoveryResult Run(string? tempFolder = null, Func<string, bool>? isOwned = null) =>
        OrphanRecordingRecovery.Run(tempFolder ?? _tempFolder, _store, Reason, isOwned ?? (_ => false),
            CaptureCoordinator.DeviceSignalThreshold);

    private void WriteSession(string baseName, DateTime? processStarted = null, DateTime? deviceStarted = null,
        DateTime? micStarted = null)
    {
        var session = new CaptureSessionInfo
        {
            AppName = "Teams",
            RecordedAt = new DateTime(2026, 10, 18, 9, 30, 0),
            ProcessStartedUtc = processStarted,
            DeviceStartedUtc = deviceStarted,
            MicStartedUtc = micStarted
        };
        File.WriteAllText(Path.Combine(_tempFolder, baseName + OrphanRecordingRecovery.SessionFileSuffix),
            JsonSerializer.Serialize(session));
    }

    private string Track(string name, double seconds, double amplitude) =>
        WavFile.WriteTone(Path.Combine(_tempFolder, name + ".wav"), seconds, amplitude, 48000, 2);
}
