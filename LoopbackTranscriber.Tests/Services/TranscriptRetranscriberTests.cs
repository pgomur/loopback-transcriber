namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class TranscriptRetranscriberTests : IDisposable
{
    private const string BaseName = "Teams_20260101_100000";
    private const string Header = "# Teams - 01/01/2026 10:00";
    private static readonly RecordingCallbacks NoCallbacks = new(_ => { }, _ => { }, _ => { });

    private readonly TempDirectory _temp = new();
    private readonly FakeWhisper _whisper = new();
    private readonly FakeAudio _audio = new();
    private readonly TranscriptRetranscriber _retranscriber;
    private readonly string _folder;

    public TranscriptRetranscriberTests()
    {
        var settings = TestSettings.In(_temp.Path, userName: "pgomur");
        _retranscriber = new TranscriptRetranscriber(new RecordingProcessor(_whisper, _audio), new FakeSettingsStore(settings));
        _folder = _temp.CreateFolder("transcripts", "Teams", BaseName);
        _whisper.ByFileName[BaseName + ".16k.wav"] = [Segment(5, 6, "Habla la reunión.")];
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task WithoutAudio_ItExplainsThatItCannotRetranscribe()
    {
        var transcript = WriteTranscript(".md", Header + "\n\nTexto viejo.");
        File.Delete(Path.Combine(_folder, BaseName + ".wav"));

        var error = await Assert.ThrowsAsync<IOException>(() => _retranscriber.RetranscribeAsync(transcript, NoCallbacks));

        Assert.True(UserError.TryGetKey(error, out var key, out _));
        Assert.Equal("ErrorRetranscribeNoAudio", key);
        Assert.False(TranscriptRetranscriber.HasAudio(transcript));
        Assert.False(TranscriptRetranscriber.HasAudio(null));
    }

    [Fact]
    public async Task AMarkdownTranscript_IsRewritten_KeepingItsHeader()
    {
        var transcript = WriteTranscript(".md", Header + "\n\nTexto viejo.");

        var result = await _retranscriber.RetranscribeAsync(transcript, NoCallbacks);

        Assert.Equal(transcript, result.FilePath);
        Assert.Equal(Header + "\n\nHabla la reunión.", result.FileText);
        Assert.Equal(result.FileText, File.ReadAllText(transcript));
        Assert.True(TranscriptRetranscriber.HasAudio(transcript));
    }

    [Fact]
    public async Task ATextTranscript_IsRewrittenWithoutHeader()
    {
        var transcript = WriteTranscript(".txt", "Texto viejo.");

        var result = await _retranscriber.RetranscribeAsync(transcript, NoCallbacks);

        Assert.Equal("Habla la reunión.", File.ReadAllText(transcript));
        Assert.Equal(transcript, result.FilePath);
    }

    [Fact]
    public async Task ARecordingWithOnlyAudio_GetsANewMarkdownTranscript()
    {
        WriteTranscript(".md", null);
        var audio = Path.Combine(_folder, BaseName + ".wav");

        var result = await _retranscriber.RetranscribeAsync(audio, NoCallbacks);

        Assert.Equal(Path.Combine(_folder, BaseName + ".md"), result.FilePath);
        Assert.Equal($"# {BaseName}\n\nHabla la reunión.", File.ReadAllText(result.FilePath));
    }

    [Fact]
    public async Task TheMicrophoneTrack_IsAlignedWithTheSavedOffset()
    {
        var transcript = WriteTranscript(".md", Header + "\n\nTexto viejo.");
        WavFile.WriteTone(Path.Combine(_folder, BaseName + "_mic.wav"), 1.0, 0.5);
        RecordingMetadata.Write(Path.Combine(_folder, BaseName + TranscriptSaver.MetadataExtension),
            new RecordingMetadata { MicStartOffsetTicks = TimeSpan.FromSeconds(10).Ticks });
        _whisper.ByFileName[BaseName + "_mic.16k.wav"] = [Segment(0, 1, "Hablo yo.")];

        var result = await _retranscriber.RetranscribeAsync(transcript, NoCallbacks);

        Assert.Equal(
            $"{Header}\n\n{SpeakerLabels.Others}: Habla la reunión.\n\npgomur (Tú): Hablo yo.",
            result.FileText);
    }

    [Fact]
    public async Task WithoutSavedOffset_TheMicrophoneIsAssumedToStartWithTheMeeting()
    {
        var transcript = WriteTranscript(".md", Header + "\n\nTexto viejo.");
        WavFile.WriteTone(Path.Combine(_folder, BaseName + "_mic.wav"), 1.0, 0.5);
        _whisper.ByFileName[BaseName + "_mic.16k.wav"] = [Segment(0, 1, "Hablo yo.")];

        var result = await _retranscriber.RetranscribeAsync(transcript, NoCallbacks);

        Assert.Equal(
            $"{Header}\n\npgomur (Tú): Hablo yo.\n\n{SpeakerLabels.Others}: Habla la reunión.",
            result.FileText);
    }

    [Fact]
    public async Task WhenNothingIsHeard_TheOldTranscriptIsKept()
    {
        var transcript = WriteTranscript(".md", Header + "\n\nTexto viejo.");
        _whisper.ByFileName.Clear();
        _audio.DurationSeconds = 3;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _retranscriber.RetranscribeAsync(transcript, NoCallbacks));

        Assert.True(UserError.TryGetKey(error, out var key, out _));
        Assert.Equal("ErrorNoTextInAudio", key);
        Assert.Equal(Header + "\n\nTexto viejo.", File.ReadAllText(transcript));
    }

    [Fact]
    public async Task TheConvertedFiles_DoNotStayNextToTheSavedRecording()
    {
        var transcript = WriteTranscript(".md", Header + "\n\nTexto viejo.");

        await _retranscriber.RetranscribeAsync(transcript, NoCallbacks);

        Assert.All(_audio.Converted, path => Assert.NotEqual(_folder, Path.GetDirectoryName(path)));
        Assert.All(_audio.Converted, path => Assert.False(File.Exists(path)));
        Assert.Empty(Directory.GetFiles(_folder, "*.16k.wav"));
    }

    private string WriteTranscript(string extension, string? content)
    {
        WavFile.WriteTone(Path.Combine(_folder, BaseName + ".wav"), 1.0, 0.5);
        var path = Path.Combine(_folder, BaseName + extension);
        if (content is not null)
        {
            File.WriteAllText(path, content);
        }

        return path;
    }

    private static TranscriptSegment Segment(double start, double end, string text) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
