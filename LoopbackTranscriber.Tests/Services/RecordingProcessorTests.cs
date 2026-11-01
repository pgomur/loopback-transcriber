namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class RecordingProcessorTests : IDisposable
{
    private static readonly RecordingCallbacks NoCallbacks = new(_ => { }, _ => { }, _ => { });

    private readonly TempDirectory _temp = new();
    private readonly FakeWhisper _whisper = new();
    private readonly FakeAudio _audio = new() { DurationSeconds = 1 };
    private readonly RecordingProcessor _processor;
    private readonly AppSettings _settings;

    public RecordingProcessorTests()
    {
        _processor = new RecordingProcessor(_whisper, _audio);
        _settings = TestSettings.In(_temp.Path, userName: "pgomur");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task OnlyAppTrack_ReturnsPlainText_AndDeletesDerivedFiles()
    {
        var source = Track("rec.wav");
        _whisper.ByFileName["rec.16k.wav"] = [Seg(0, 2, "Hola a todos."), Seg(2, 4, "Empezamos.")];

        var text = await _processor.ProcessAsync(source, null, TimeSpan.Zero, _settings, NoCallbacks);

        Assert.Equal("Hola a todos. Empezamos.", text);
        Assert.False(File.Exists(_temp.Combine("rec.16k.wav")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task AppAndMicTracks_AreInterleavedWithSpeakerLabels()
    {
        var source = Track("app.wav");
        var mic = Track("mic.wav");
        _whisper.ByFileName["app.16k.wav"] = [Seg(0, 2, "Buenos dias equipo.")];
        _whisper.ByFileName["mic.16k.wav"] = [Seg(0, 2, "Presupuesto aprobado para el trimestre.")];

        var text = await _processor.ProcessAsync(source, mic, TimeSpan.FromSeconds(10), _settings, NoCallbacks);

        Assert.Equal(
            $"{SpeakerLabels.Others}: Buenos dias equipo.\n\n{SpeakerLabels.ForSelf("pgomur")}: Presupuesto aprobado para el trimestre.",
            text);
        Assert.False(File.Exists(_temp.Combine("app.16k.wav")));
        Assert.False(File.Exists(_temp.Combine("mic.16k.wav")));
    }

    [Fact]
    public async Task OnlyTheMicrophoneTrack_GoesThroughTheNoiseGate()
    {
        var source = Track("app.wav");
        var mic = Track("mic.wav");

        await _processor.ProcessAsync(source, mic, TimeSpan.Zero, _settings, NoCallbacks);

        Assert.Equal(_temp.Combine("mic.16k.wav"), Assert.Single(_audio.NoiseGated));
    }

    [Fact]
    public async Task ANoiseGateFailure_DoesNotStopTheTranscription()
    {
        var source = Track("app.wav");
        var mic = Track("mic.wav");
        _audio.NoiseGateFails = true;
        _whisper.ByFileName["mic.16k.wav"] = [Seg(0, 2, "Sigo hablando aunque el filtro falle.")];

        var text = await _processor.ProcessAsync(source, mic, TimeSpan.Zero, _settings, NoCallbacks);

        Assert.Contains("Sigo hablando", text);
    }

    [Fact]
    public async Task MicrophoneSegmentsWithoutVoiceActivity_AreDropped()
    {
        var source = Track("app.wav");
        var mic = Track("mic.wav");
        _whisper.ByFileName["app.16k.wav"] = [Seg(0, 2, "Hola.")];
        _whisper.ByFileName["mic.16k.wav"] = [Seg(5, 7, "Esto lo invento Whisper sobre ruido.")];
        _audio.ActivityMap = new bool[200];

        var text = await _processor.ProcessAsync(source, mic, TimeSpan.Zero, _settings, NoCallbacks);

        Assert.Equal("Hola.", text);
    }

    [Fact]
    public async Task MicrophoneSegmentsWithVoiceActivity_AreKept()
    {
        var source = Track("app.wav");
        var mic = Track("mic.wav");
        _whisper.ByFileName["mic.16k.wav"] = [Seg(5, 7, "Esto lo dije yo.")];
        _audio.ActivityMap = Enumerable.Repeat(true, 200).ToArray();

        var text = await _processor.ProcessAsync(source, mic, TimeSpan.Zero, _settings, NoCallbacks);

        Assert.Equal($"{SpeakerLabels.ForSelf("pgomur")}: Esto lo dije yo.", text);
    }

    [Fact]
    public async Task WithAutomaticLanguage_TheMicrophoneUsesTheLanguageDetectedInTheMeeting()
    {
        _settings.MeetingLanguage = AppSettings.MeetingLanguageAuto;
        _whisper.DetectedLanguageByFileName["app.16k.wav"] = "en";

        await _processor.ProcessAsync(Track("app.wav"), Track("mic.wav"), TimeSpan.Zero, _settings, NoCallbacks);

        var requests = _whisper.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Equal("app.16k.wav", Path.GetFileName(requests[0].WavPath));
        Assert.Equal(AppSettings.MeetingLanguageAuto, requests[0].Language);
        Assert.False(requests[0].StrictNoSpeech);
        Assert.Equal("mic.16k.wav", Path.GetFileName(requests[1].WavPath));
        Assert.Equal("en", requests[1].Language);
        Assert.True(requests[1].StrictNoSpeech);
        Assert.All(requests, r => Assert.Equal("es", r.FallbackLanguage));
        Assert.All(requests, r => Assert.Equal(_settings.GetModelFilePath(), r.ModelPath));
    }

    [Fact]
    public async Task WithAChosenLanguage_BothTracksUseIt()
    {
        _settings.MeetingLanguage = "de";

        await _processor.ProcessAsync(Track("app.wav"), Track("mic.wav"), TimeSpan.Zero, _settings, NoCallbacks);

        Assert.All(_whisper.Requests, r => Assert.Equal("de", r.Language));
        Assert.Equal(2, _whisper.Requests.Count);
    }

    [Fact]
    public async Task WithoutAMeetingLanguage_TheInterfaceLanguageIsUsed()
    {
        _settings.MeetingLanguage = string.Empty;
        _settings.UiLanguage = "pt";

        await _processor.ProcessAsync(Track("app.wav"), null, TimeSpan.Zero, _settings, NoCallbacks);

        Assert.Equal("pt", Assert.Single(_whisper.Requests).Language);
    }

    [Fact]
    public async Task WithAWorkFolder_TheConvertedFilesAreCreatedThere_AndRemovedAfterwards()
    {
        var source = Track("app.wav");
        var mic = Track("mic.wav");
        var workFolder = _temp.Combine("work");

        await _processor.ProcessAsync(source, mic, TimeSpan.Zero, _settings, NoCallbacks, workFolder);

        Assert.All(_audio.Converted, path => Assert.Equal(workFolder, Path.GetDirectoryName(path)));
        Assert.Empty(Directory.GetFiles(workFolder));
        Assert.Equal(2, Directory.GetFiles(_temp.Path, "*.wav").Length);
    }

    [Fact]
    public async Task TheTranscriptionStateIsReported()
    {
        var active = new System.Collections.Concurrent.ConcurrentQueue<bool>();
        var statuses = new System.Collections.Concurrent.ConcurrentQueue<string?>();
        var callbacks = new RecordingCallbacks(m => statuses.Enqueue(m.Key), _ => { }, active.Enqueue);
        _whisper.ByFileName["rec.16k.wav"] = [Seg(0, 1, "Hola.")];

        await _processor.ProcessAsync(Track("rec.wav"), null, TimeSpan.Zero, _settings, callbacks);

        Assert.Equal(new[] { true, false }, active);
        Assert.Contains("StatusCheckingModel", statuses);
        Assert.Contains("StatusTranscribingWithPercent", statuses);
    }

    [Fact]
    public async Task EmptyTextInLongAudio_IsAnError()
    {
        _audio.DurationSeconds = 120;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _processor.ProcessAsync(Track("rec.wav"), null, TimeSpan.Zero, _settings, NoCallbacks));

        Assert.True(UserError.TryGetKey(error, out var key, out var args));
        Assert.Equal("ErrorNoTextInAudio", key);
        Assert.Equal(new object?[] { 120 }, args);
        Assert.False(File.Exists(_temp.Combine("rec.16k.wav")));
    }

    [Fact]
    public async Task EmptyTextInShortAudio_IsNotAnError()
    {
        _audio.DurationSeconds = RecordingProcessor.MinSecondsForEmptyTranscriptToBeAnError - 1;

        var text = await _processor.ProcessAsync(Track("rec.wav"), null, TimeSpan.Zero, _settings, NoCallbacks);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task EngineFailure_PropagatesAndStillDeletesDerivedFiles()
    {
        var source = Track("rec.wav");
        _whisper.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _processor.ProcessAsync(source, null, TimeSpan.Zero, _settings, NoCallbacks));

        Assert.False(File.Exists(_temp.Combine("rec.16k.wav")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Cancellation_StopsTheTranscription_AndCleansUp()
    {
        _whisper.Gate = new TaskCompletionSource();
        using var cancellation = new CancellationTokenSource();

        var processing = _processor.ProcessAsync(Track("rec.wav"), null, TimeSpan.Zero, _settings, NoCallbacks,
            cancellationToken: cancellation.Token);
        await Eventually.Until(() => _whisper.Requests.Count == 1, "the transcription started");
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        Assert.False(File.Exists(_temp.Combine("rec.16k.wav")));
    }

    private string Track(string name) => _temp.WriteBytes(name, [1]);

    private static TranscriptSegment Seg(double start, double end, string text) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
