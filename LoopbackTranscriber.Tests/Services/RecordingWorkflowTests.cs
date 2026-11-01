using System.Text.Json;

namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class RecordingWorkflowTests : IDisposable
{
    private static readonly RecordingCallbacks NoCallbacks = new(_ => { }, _ => { }, _ => { });

    private readonly TempDirectory _temp = new();
    private readonly FakeProcess _process = new();
    private readonly FakeDevice _device = new();
    private readonly FakeMic _mic = new();
    private readonly FakeWhisper _whisper = new();
    private readonly FakeAudio _audio = new();
    private readonly FailedRecordingStore _failed;
    private readonly RecordingWorkflow _workflow;
    private readonly CaptureCoordinator _capture;
    private readonly string _tempFolder;
    private readonly string _transcripts;

    public RecordingWorkflowTests()
    {
        _tempFolder = _temp.Combine("tmp");
        _failed = new FailedRecordingStore(_temp.Combine("failed"));
        var settings = TestSettings.In(_temp.Path, userName: "pgomur");
        _transcripts = settings.TranscriptsFolder;

        _capture = new CaptureCoordinator(_process, _device, _mic, tempFolder: _tempFolder, failedStore: _failed);
        _workflow = new RecordingWorkflow(_capture, new RecordingProcessor(_whisper, _audio), new TranscriptSaver(),
            new FakeSettingsStore(settings), _failed);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _temp.Dispose();
    }

    private void WhisperSays(string text)
    {
        _whisper.ByFileName.Clear();
        var name = Path.GetFileNameWithoutExtension(_process.WavPath) + ".16k.wav";
        _whisper.ByFileName[name] = [Seg(0, 2, text)];
    }

    [Fact]
    public async Task SuccessfulRecording_SavesTranscriptAndAudio_AndCleansTemporaryFiles()
    {
        var recordedAt = new DateTime(2026, 10, 18, 9, 30, 0);
        await _workflow.StartAsync(1234, "Teams");
        WhisperSays("Hola a todos.");

        var result = await _workflow.StopAndProcessAsync("Teams", recordedAt, NoCallbacks);

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Equal("Hola a todos.", result.Transcript!.Text);
        Assert.NotNull(result.Transcript.MarkdownPath);
        Assert.Equal(TranscriptSaver.BuildHeader("Teams", recordedAt) + "\n\nHola a todos.", File.ReadAllText(result.Transcript.MarkdownPath!));
        Assert.True(File.Exists(Path.ChangeExtension(result.Transcript.MarkdownPath!, ".wav")));
        Assert.Empty(Directory.GetFiles(_tempFolder));
        Assert.Empty(_workflow.ListFailed());
    }

    [Fact]
    public async Task StopAndPreserve_KeepsTheAudioAsAnUnprocessedRecording_WithoutTranscribing()
    {
        var recordedAt = new DateTime(2026, 10, 18, 9, 30, 0);
        await _workflow.StartAsync(1234);

        var result = await _workflow.StopAndPreserveAsync("Teams", recordedAt, "se cerro la sesion");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Preserved);
        Assert.Equal("se cerro la sesion", result.Error);
        var kept = Assert.Single(_failed.List());
        Assert.Equal("Teams", kept.AppName);
        Assert.True(File.Exists(kept.SourceWavPath));
        Assert.Empty(Directory.GetFiles(_tempFolder));
        Assert.True(_process.Stopped);
        Assert.Empty(_audio.Converted);
    }

    [Fact]
    public async Task EmptyTextInShortAudio_Succeeds_WithoutSavingAnything()
    {
        _audio.DurationSeconds = 3;
        await _workflow.StartAsync(1234);

        var result = await _workflow.StopAndProcessAsync("Teams", DateTime.Now, NoCallbacks);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Transcript!.Text);
        Assert.Null(result.Transcript.MarkdownPath);
        Assert.False(Directory.Exists(_transcripts));
        Assert.Empty(Directory.GetFiles(_tempFolder));
    }

    [Fact]
    public async Task EngineFailure_KeepsTheAudioForRetry_AndRetrySucceedsAndClearsIt()
    {
        var recordedAt = new DateTime(2026, 10, 18, 10, 0, 0);
        await _workflow.StartAsync(1234);
        _whisper.Fail = true;

        var failed = await _workflow.StopAndProcessAsync("Teams", recordedAt, NoCallbacks);

        Assert.False(failed.Succeeded);
        Assert.NotNull(failed.Preserved);
        Assert.Contains("fallo simulado", failed.Error);
        Assert.Empty(Directory.GetFiles(_tempFolder));

        var pending = Assert.Single(_workflow.ListFailed());
        Assert.Equal("Teams", pending.AppName);
        Assert.True(File.Exists(pending.SourceWavPath));

        _whisper.Fail = false;
        _whisper.ByFileName["meeting.16k.wav"] = [Seg(0, 2, "Texto recuperado.")];

        var retried = await _workflow.RetryAsync(pending, NoCallbacks);

        Assert.True(retried.Succeeded);
        Assert.Equal("Texto recuperado.", retried.Transcript!.Text);
        Assert.Empty(_workflow.ListFailed());
        Assert.True(File.Exists(retried.Transcript.MarkdownPath));
    }

    [Fact]
    public async Task Retry_KeepsTheMicrophoneTrackAndItsOffset()
    {
        _process.FirstSampleTimestamp = TimeSpan.FromSeconds(100).Ticks;
        _mic.FirstSampleTimestamp = TimeSpan.FromSeconds(110).Ticks;
        await _workflow.StartAsync(1234);
        _whisper.Fail = true;
        await _workflow.StopAndProcessAsync("Teams", DateTime.Now, NoCallbacks);
        var pending = Assert.Single(_workflow.ListFailed());
        Assert.Equal(TimeSpan.FromSeconds(10), pending.MicStartOffset);

        _whisper.Fail = false;
        _whisper.ByFileName["meeting.16k.wav"] = [Seg(5, 6, "Reunión.")];
        _whisper.ByFileName["microphone.16k.wav"] = [Seg(0, 1, "Yo.")];
        var retried = await _workflow.RetryAsync(pending, NoCallbacks);

        Assert.Equal($"{SpeakerLabels.Others}: Reunión.\n\npgomur (Tú): Yo.", retried.Transcript!.Text);
    }

    [Fact]
    public async Task RetryThatFailsAgain_KeepsTheRecording()
    {
        await _workflow.StartAsync(1234);
        _whisper.Fail = true;
        await _workflow.StopAndProcessAsync("Teams", DateTime.Now, NoCallbacks);
        var pending = Assert.Single(_workflow.ListFailed());

        var retried = await _workflow.RetryAsync(pending, NoCallbacks);

        Assert.False(retried.Succeeded);
        Assert.Null(retried.Preserved);
        Assert.NotNull(retried.Error);
        Assert.Single(_workflow.ListFailed());
    }

    [Fact]
    public async Task NoAudioFromAnyCapture_FailsWithoutPreservingAnything()
    {
        await _workflow.StartAsync(1234);
        _process.FailStop = true;
        _device.FailStop = true;
        _mic.FailStop = true;

        var result = await _workflow.StopAndProcessAsync("Teams", DateTime.Now, NoCallbacks);

        Assert.False(result.Succeeded);
        Assert.Null(result.Preserved);
        Assert.NotNull(result.Error);
        Assert.Empty(_workflow.ListFailed());
    }

    [Fact]
    public async Task CaptureWarnings_ReachTheResult()
    {
        _mic.StartSucceeds = false;

        var startWarnings = await _workflow.StartAsync(1234);
        WhisperSays("Hola.");
        var result = await _workflow.StopAndProcessAsync("Teams", DateTime.Now, NoCallbacks);

        Assert.Equal("WarningMicNotRecorded", Assert.Single(startWarnings).Key);
        Assert.Equal("WarningMicNotRecorded", Assert.Single(result.Warnings!).Key);
    }

    [Fact]
    public async Task DiscardFailed_RemovesTheRecording()
    {
        await _workflow.StartAsync(1234);
        _whisper.Fail = true;
        await _workflow.StopAndProcessAsync("Teams", DateTime.Now, NoCallbacks);

        _workflow.DiscardFailed(Assert.Single(_workflow.ListFailed()));

        Assert.Empty(_workflow.ListFailed());
    }

    [Fact]
    public void RecoverOrphans_TurnsAnInterruptedRecordingIntoAPendingOne()
    {
        Directory.CreateDirectory(_tempFolder);
        File.WriteAllText(Path.Combine(_tempFolder, "recording_20261018_093000" + OrphanRecordingRecovery.SessionFileSuffix),
            JsonSerializer.Serialize(new CaptureSessionInfo { AppName = "Teams", RecordedAt = new DateTime(2026, 10, 18, 9, 30, 0) }));
        WavFile.WriteTone(Path.Combine(_tempFolder, "recording_20261018_093000.wav"), 0.5, 0.5);

        var result = _workflow.RecoverOrphans();

        Assert.Single(result.Recovered);
        var pending = Assert.Single(_workflow.ListFailed());
        Assert.Equal("RecordingRecoveredReason", pending.ErrorKey);
    }

    [Fact]
    public void LiveAudio_IsForwardedThroughTheWorkflow()
    {
        byte[]? received = null;
        _workflow.AudioCaptured += (buffer, _) => received = buffer;

        _process.Raise([7, 8]);

        Assert.Equal(new byte[] { 7, 8 }, received);
    }

    private static TranscriptSegment Seg(double start, double end, string text) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
