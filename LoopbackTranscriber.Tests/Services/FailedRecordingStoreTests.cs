namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class FailedRecordingStoreTests : IDisposable
{
    private static readonly DateTime RecordedAt = new(2026, 10, 18, 9, 30, 0);

    private readonly TempDirectory _temp = new();
    private readonly UserNotices _notices = new();
    private readonly FailedRecordingStore _store;

    public FailedRecordingStoreTests()
    {
        _store = new FailedRecordingStore(_temp.Combine("failed"), _notices);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void TryPreserve_WithoutAudio_KeepsNothing()
    {
        Assert.Null(_store.TryPreserve(null, null, TimeSpan.Zero, "Teams", RecordedAt, "error"));
        Assert.Null(_store.TryPreserve(_temp.Combine("missing.wav"), null, TimeSpan.Zero, "Teams", RecordedAt, "error"));
        Assert.Empty(_store.List());
    }

    [Fact]
    public void TryPreserve_MovesBothTracks_AndRemembersEverythingNeededToRetry()
    {
        var source = WavFile.WriteTone(_temp.Combine("tmp", "recording_1.wav"), 1.0, 0.5);
        var mic = WavFile.WriteTone(_temp.Combine("tmp", "recording_1_mic.wav"), 1.0, 0.5);

        var saved = _store.TryPreserve(source, mic, TimeSpan.FromMilliseconds(250), "Teams", RecordedAt,
            UserMessage.FromKey("ErrorNoTextInAudio", 30));

        Assert.NotNull(saved);
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(mic));
        Assert.True(File.Exists(saved.SourceWavPath));
        Assert.True(File.Exists(saved.MicWavPath));
        Assert.StartsWith("20261018_093000_", Path.GetFileName(saved.Folder));

        var listed = Assert.Single(_store.List());
        Assert.Equal("Teams", listed.AppName);
        Assert.Equal(RecordedAt, listed.RecordedAt);
        Assert.Equal(TimeSpan.FromMilliseconds(250), listed.MicStartOffset);
        Assert.Equal("ErrorNoTextInAudio", listed.ErrorKey);
        Assert.Equal(new[] { "30" }, listed.ErrorArgs);
        Assert.Equal(Strings.Instance.FormatIn("en", "ErrorNoTextInAudio", 30), listed.Error);
        var duration = Assert.IsType<TimeSpan>(listed.Duration);
        Assert.InRange(duration.TotalSeconds, 0.99, 1.01);
    }

    [Fact]
    public void TryPreserve_WithoutMicrophone_KeepsOnlyTheMeeting()
    {
        var source = WavFile.WriteTone(_temp.Combine("tmp", "recording_1.wav"), 0.5, 0.5);

        var saved = _store.TryPreserve(source, _temp.Combine("tmp", "missing_mic.wav"), TimeSpan.Zero, "Zoom", RecordedAt, "boom");

        Assert.NotNull(saved);
        Assert.Null(saved.MicWavPath);
        var listed = Assert.Single(_store.List());
        Assert.Null(listed.MicWavPath);
        Assert.Null(listed.ErrorKey);
        Assert.Equal("boom", listed.Error);
    }

    [Fact]
    public void List_ShowsTheNewestFirst()
    {
        Preserve("Teams", new DateTime(2026, 1, 1, 9, 0, 0));
        Preserve("Zoom", new DateTime(2026, 3, 1, 9, 0, 0));
        Preserve("Meet", new DateTime(2026, 2, 1, 9, 0, 0));

        Assert.Equal(new[] { "Zoom", "Meet", "Teams" }, _store.List().Select(r => r.AppName));
    }

    [Fact]
    public void List_IgnoresFoldersWithoutManifestOrWithoutAudio()
    {
        var kept = Preserve("Teams", RecordedAt);
        var lost = Preserve("Zoom", RecordedAt.AddHours(1));
        File.Delete(lost.SourceWavPath);
        _temp.CreateFolder("failed", "carpeta_vacia");

        Assert.Equal(kept.Folder, Assert.Single(_store.List()).Folder);
        Assert.Empty(_notices.TakeAll());
    }

    [Fact]
    public void List_ForgetsAMicrophoneTrackThatNoLongerExists()
    {
        var saved = Preserve("Teams", RecordedAt, withMic: true);
        File.Delete(saved.MicWavPath!);

        Assert.Null(Assert.Single(_store.List()).MicWavPath);
    }

    [Fact]
    public void List_ReportsAnUnreadableManifestOnce_AndStillShowsTheOthers()
    {
        Preserve("Teams", RecordedAt);
        var broken = Preserve("Zoom", RecordedAt.AddHours(1));
        File.WriteAllText(Path.Combine(broken.Folder, "manifest.json"), "{ esto no es json");

        Assert.Equal("Teams", Assert.Single(_store.List()).AppName);
        _store.List();

        var notice = Assert.Single(_notices.TakeAll());
        Assert.Equal("NoticeUnreadableFailedRecording", notice.Key);
    }

    [Fact]
    public void List_WithoutTheRootFolder_IsEmpty()
    {
        Assert.Empty(new FailedRecordingStore(_temp.Combine("does-not-exist")).List());
    }

    [Fact]
    public void List_GivesNoDurationForABrokenWav_ButStillListsIt()
    {
        var source = _temp.WriteText(Path.Combine("tmp", "broken.wav"), "no es un wav");
        _store.TryPreserve(source, null, TimeSpan.Zero, "Teams", RecordedAt, "error");

        Assert.Null(Assert.Single(_store.List()).Duration);
    }

    [Fact]
    public void DeleteAndTryDelete_RemoveTheRecording()
    {
        var first = Preserve("Teams", RecordedAt);
        var second = Preserve("Zoom", RecordedAt.AddHours(1));

        _store.Delete(first);
        _store.TryDelete(second);
        _store.TryDelete(second);

        Assert.Empty(_store.List());
        Assert.False(Directory.Exists(first.Folder));
    }

    private FailedRecording Preserve(string appName, DateTime recordedAt, bool withMic = false)
    {
        var name = Guid.NewGuid().ToString("N");
        var source = WavFile.WriteTone(_temp.Combine("tmp", name + ".wav"), 0.2, 0.5);
        var mic = withMic ? WavFile.WriteTone(_temp.Combine("tmp", name + "_mic.wav"), 0.2, 0.5) : null;
        return _store.TryPreserve(source, mic, TimeSpan.Zero, appName, recordedAt, "error")
               ?? throw new InvalidOperationException("The test recording could not be preserved.");
    }
}
