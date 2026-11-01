using System.Text.Json;

namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class CaptureCoordinatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeProcess _process = new();
    private readonly FakeDevice _device = new();
    private readonly FakeMic _mic = new();
    private readonly FailedRecordingStore _store;
    private readonly CaptureCoordinator _coordinator;
    private readonly string _tempFolder;

    public CaptureCoordinatorTests()
    {
        _tempFolder = _temp.Combine("tmp");
        _store = new FailedRecordingStore(_temp.Combine("failed"));
        _coordinator = new CaptureCoordinator(_process, _device, _mic, SteppingClock(), _tempFolder, _store);
    }

    public void Dispose()
    {
        _coordinator.Dispose();
        _temp.Dispose();
    }

    private static Func<DateTime> SteppingClock()
    {
        var t = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        return () => t = t.AddSeconds(1);
    }

    [Fact]
    public async Task ProcessCaptureFailingToStart_Throws()
    {
        _process.FailStart = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _coordinator.StartAsync(1234));
    }

    [Fact]
    public async Task Start_WritesASessionFileSoTheRecordingCanBeRecoveredAfterACrash()
    {
        var warnings = await _coordinator.StartAsync(1234, "Teams");

        Assert.Empty(warnings);
        var sessionFile = Assert.Single(Directory.GetFiles(_tempFolder, "*" + OrphanRecordingRecovery.SessionFileSuffix));
        Assert.Equal(Path.GetFileNameWithoutExtension(_process.WavPath) + OrphanRecordingRecovery.SessionFileSuffix,
            Path.GetFileName(sessionFile));
        var session = JsonSerializer.Deserialize<CaptureSessionInfo>(File.ReadAllText(sessionFile))!;
        Assert.Equal("Teams", session.AppName);
        Assert.NotNull(session.ProcessStartedUtc);
        Assert.NotNull(session.DeviceStartedUtc);
        Assert.NotNull(session.MicStartedUtc);
        Assert.StartsWith(RecordingNames.FilePrefix, Path.GetFileName(_process.WavPath));
    }

    [Fact]
    public async Task Start_WithoutAppName_UsesTheDefaultName()
    {
        await _coordinator.StartAsync(1234);

        var sessionFile = Assert.Single(Directory.GetFiles(_tempFolder, "*" + OrphanRecordingRecovery.SessionFileSuffix));
        Assert.Equal(RecordingNames.DefaultAppName, JsonSerializer.Deserialize<CaptureSessionInfo>(File.ReadAllText(sessionFile))!.AppName);
    }

    [Fact]
    public async Task DeviceAndMicFailingToStart_AreTolerated_AndTheMissingMicrophoneIsReported()
    {
        _device.FailStart = true;
        _mic.FailStart = true;

        var warnings = await _coordinator.StartAsync(1234);
        var stopped = await _coordinator.StopAsync();

        Assert.Equal("WarningMicNotRecorded", Assert.Single(warnings).Key);
        Assert.NotNull(stopped.SourceWavPath);
        Assert.Null(stopped.MicWavPath);
        Assert.Equal("WarningMicNotRecorded", Assert.Single(stopped.Warnings!).Key);
    }

    [Fact]
    public async Task AMicrophoneThatDoesNotStart_IsReported()
    {
        _mic.StartSucceeds = false;

        var warnings = await _coordinator.StartAsync(1234);

        Assert.Equal("WarningMicNotRecorded", Assert.Single(warnings).Key);
    }

    [Fact]
    public async Task AMicrophoneThatFailsToStop_IsReported()
    {
        await _coordinator.StartAsync(1234);
        _mic.FailStop = true;

        var stopped = await _coordinator.StopAsync();

        Assert.Null(stopped.MicWavPath);
        Assert.Equal("WarningMicNotRecorded", Assert.Single(stopped.Warnings!).Key);
    }

    [Fact]
    public async Task WarningsFromThePreviousRecording_AreNotRepeated()
    {
        _mic.StartSucceeds = false;
        await _coordinator.StartAsync(1234);
        await _coordinator.StopAsync();
        _mic.StartSucceeds = true;

        var warnings = await _coordinator.StartAsync(1234);

        Assert.Empty(warnings);
    }

    [Fact]
    public async Task Stop_StopsAllThreeCapturesEvenWhenTheProcessOneFails()
    {
        await _coordinator.StartAsync(1234);
        _process.FailStop = true;
        _device.Tracks.Add(new DeviceLoopbackRecorder.DeviceTrack { Label = "altavoces", WavPath = "dev.wav", PeakAbs = 0.4 });

        var stopped = await _coordinator.StopAsync();

        Assert.True(_process.Stopped);
        Assert.True(_device.Stopped);
        Assert.True(_mic.Stopped);
        Assert.NotNull(stopped.ProcessStopError);
        Assert.Equal("dev.wav", stopped.SourceWavPath);
    }

    [Fact]
    public async Task Stop_WithNoUsableTrack_ReturnsNoSource_AndKeepsTheError()
    {
        await _coordinator.StartAsync(1234);
        _process.FailStop = true;
        _device.FailStop = true;
        _mic.FailStop = true;

        var stopped = await _coordinator.StopAsync();

        Assert.Null(stopped.SourceWavPath);
        Assert.Null(stopped.MicWavPath);
        Assert.NotNull(stopped.ProcessStopError);
    }

    [Fact]
    public async Task Stop_PicksTheLoudestDeviceTrackAboveTheThreshold_WhenTheAppTrackIsSilent()
    {
        await _coordinator.StartAsync(1234);
        _process.LastPeakAbs = 0;
        _device.Tracks.Add(new DeviceLoopbackRecorder.DeviceTrack { Label = "a", WavPath = "quiet.wav", PeakAbs = 0.005 });
        _device.Tracks.Add(new DeviceLoopbackRecorder.DeviceTrack { Label = "b", WavPath = "mid.wav", PeakAbs = 0.2 });
        _device.Tracks.Add(new DeviceLoopbackRecorder.DeviceTrack { Label = "c", WavPath = "loud.wav", PeakAbs = 0.7 });

        var stopped = await _coordinator.StopAsync();

        Assert.Equal("loud.wav", stopped.SourceWavPath);
        Assert.Contains("quiet.wav", stopped.AllWavPaths);
    }

    [Fact]
    public async Task Stop_KeepsTheAppTrack_WhenItHasSignal()
    {
        await _coordinator.StartAsync(1234);
        _device.Tracks.Add(new DeviceLoopbackRecorder.DeviceTrack { Label = "c", WavPath = "loud.wav", PeakAbs = 0.9 });

        var stopped = await _coordinator.StopAsync();

        Assert.Equal(_process.WavPath, stopped.SourceWavPath);
    }

    [Fact]
    public async Task Stop_WithOnlySilentDeviceTracks_FallsBackToTheProcessTrack()
    {
        await _coordinator.StartAsync(1234);
        _process.LastPeakAbs = 0;
        _device.Tracks.Add(new DeviceLoopbackRecorder.DeviceTrack { Label = "a", WavPath = "quiet.wav", PeakAbs = 0.001 });

        var stopped = await _coordinator.StopAsync();

        Assert.Equal(_process.WavPath, stopped.SourceWavPath);
    }

    [Fact]
    public async Task MicOffset_IsMeasuredAgainstTheTrackThatWasChosen()
    {
        await _coordinator.StartAsync(1234);
        var withProcessTrack = await _coordinator.StopAsync();
        Assert.Equal(TimeSpan.FromSeconds(2), withProcessTrack.MicStartOffset);

        await _coordinator.StartAsync(1234);
        _process.LastPeakAbs = 0;
        _device.Tracks.Add(new DeviceLoopbackRecorder.DeviceTrack { Label = "a", WavPath = "dev.wav", PeakAbs = 0.5 });
        var withDeviceTrack = await _coordinator.StopAsync();
        Assert.Equal(TimeSpan.FromSeconds(1), withDeviceTrack.MicStartOffset);
    }

    [Fact]
    public async Task MicOffset_PrefersTheTimestampsOfTheFirstSamples()
    {
        _process.FirstSampleTimestamp = TimeSpan.FromSeconds(100).Ticks;
        _mic.FirstSampleTimestamp = TimeSpan.FromSeconds(100.25).Ticks;
        await _coordinator.StartAsync(1234);

        var stopped = await _coordinator.StopAsync();

        Assert.Equal(TimeSpan.FromMilliseconds(250), stopped.MicStartOffset);
    }

    [Fact]
    public void LiveAudio_IsForwardedFromTheProcessCapture()
    {
        byte[]? received = null;
        _coordinator.AudioCaptured += (buffer, _) => received = buffer;

        _process.Raise([1, 2, 3]);

        Assert.Equal(new byte[] { 1, 2, 3 }, received);
    }

    [Fact]
    public void PreserveFailed_WithNoStoppedRecording_KeepsEverything()
    {
        var outcome = _coordinator.PreserveFailed(null, "Teams", DateTime.Now, "error");

        Assert.Null(outcome.Saved);
        Assert.True(outcome.KeepOriginals);
    }

    [Fact]
    public async Task PreserveFailed_MovesTheAudioToTheFailedStore_AndLeavesNothingInTemp()
    {
        await _coordinator.StartAsync(1234);
        var stopped = await _coordinator.StopAsync();
        Assert.True(File.Exists(stopped.SourceWavPath));

        var outcome = _coordinator.PreserveFailed(stopped, "Teams", new DateTime(2026, 10, 18, 9, 0, 0), "boom");
        _coordinator.DeleteOriginals(stopped);

        Assert.NotNull(outcome.Saved);
        Assert.False(outcome.KeepOriginals);
        Assert.Empty(Directory.GetFiles(_tempFolder));
        var listed = Assert.Single(_store.List());
        Assert.Equal("Teams", listed.AppName);
        Assert.Equal("boom", listed.Error);
        Assert.True(File.Exists(listed.SourceWavPath));
        Assert.NotNull(listed.MicWavPath);
    }

    [Fact]
    public async Task PreserveFailed_WithOnlyTheMicrophone_KeepsItAsTheSource()
    {
        await _coordinator.StartAsync(1234);
        _process.FailStop = true;
        var stopped = await _coordinator.StopAsync();

        var outcome = _coordinator.PreserveFailed(stopped, "Teams", DateTime.Now, "boom");

        Assert.NotNull(outcome.Saved);
        Assert.Null(outcome.Saved.MicWavPath);
    }

    [Fact]
    public void PreserveFailed_WhenNothingCanBeSaved_KeepsTheOriginalsAndPointsToThem()
    {
        var stopped = new StoppedRecording(_temp.Combine("tmp", "missing.wav"), null, TimeSpan.Zero,
            [_temp.Combine("tmp", "missing.wav")], null);

        var outcome = _coordinator.PreserveFailed(stopped, "Teams", DateTime.Now, "boom");

        Assert.Null(outcome.Saved);
        Assert.True(outcome.KeepOriginals);
        Assert.Equal(_tempFolder, outcome.TempFolder);
    }

    [Fact]
    public async Task DeleteOriginals_RemovesTheRecordingFilesAndItsSessionFile()
    {
        await _coordinator.StartAsync(1234);
        var stopped = await _coordinator.StopAsync();

        _coordinator.DeleteOriginals(stopped);

        Assert.Empty(Directory.GetFiles(_tempFolder));
    }

    [Fact]
    public async Task RecoverOrphans_LeavesTheRecordingInProgressAlone_ButRecoversOlderOnes()
    {
        Directory.CreateDirectory(_tempFolder);
        File.WriteAllText(Path.Combine(_tempFolder, "recording_20200101_090000" + OrphanRecordingRecovery.SessionFileSuffix),
            JsonSerializer.Serialize(new CaptureSessionInfo { AppName = "Zoom", RecordedAt = new DateTime(2020, 1, 1, 9, 0, 0) }));
        WavFile.WriteTone(Path.Combine(_tempFolder, "recording_20200101_090000.wav"), 0.5, 0.5);

        await _coordinator.StartAsync(1234, "Teams");
        WavFile.WriteTone(_process.WavPath, 0.5, 0.5);

        var result = _coordinator.RecoverOrphans(UserMessage.FromKey("RecordingRecoveredReason"));

        Assert.Equal("Zoom", Assert.Single(result.Recovered).AppName);
        Assert.True(File.Exists(_process.WavPath));
    }

    [Fact]
    public async Task AfterTheRecordingIsDeleted_ItIsNoLongerProtectedFromRecovery()
    {
        await _coordinator.StartAsync(1234, "Teams");
        var stopped = await _coordinator.StopAsync();
        _coordinator.DeleteOriginals(stopped);
        File.WriteAllText(Path.Combine(_tempFolder, stopped.BaseName + OrphanRecordingRecovery.SessionFileSuffix),
            JsonSerializer.Serialize(new CaptureSessionInfo { AppName = "Teams", RecordedAt = DateTime.Now }));
        WavFile.WriteTone(Path.Combine(_tempFolder, stopped.BaseName + ".wav"), 0.5, 0.5);

        var result = _coordinator.RecoverOrphans(UserMessage.FromKey("RecordingRecoveredReason"));

        Assert.Single(result.Recovered);
    }

    [Fact]
    public void DeleteStaleRecordings_OnlyRemovesOldConvertedCopies_NeverTheOriginalAudio()
    {
        Directory.CreateDirectory(_tempFolder);
        var oldConverted = Old(WavFile.WriteTone(Path.Combine(_tempFolder, "recording_20200101_090000.16k.wav"), 0.1, 0.5));
        var oldOriginal = Old(WavFile.WriteTone(Path.Combine(_tempFolder, "recording_20200101_090000.wav"), 0.1, 0.5));
        var oldSession = Old(_temp.WriteText(Path.Combine("tmp", "recording_20200101_090000" + OrphanRecordingRecovery.SessionFileSuffix), "{}"));
        var recentConverted = WavFile.WriteTone(Path.Combine(_tempFolder, "recording_20261017_090000.16k.wav"), 0.1, 0.5);

        _coordinator.DeleteStaleRecordings();

        Assert.False(File.Exists(oldConverted));
        Assert.True(File.Exists(oldOriginal));
        Assert.True(File.Exists(oldSession));
        Assert.True(File.Exists(recentConverted));
    }

    private static string Old(string path)
    {
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-8));
        return path;
    }
}
