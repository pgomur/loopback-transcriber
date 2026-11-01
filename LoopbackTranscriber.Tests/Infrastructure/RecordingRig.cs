using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;

namespace LoopbackTranscriber.Tests.Infrastructure;

internal sealed class RecordingRig : IDisposable
{
    public RecordingRig(string userName = "Pedro")
    {
        Settings = TestSettings.In(Temp.Path, userName);
        SettingsStore = new FakeSettingsStore(Settings);
        TempFolder = Temp.Combine("tmp");
        Failed = new FailedRecordingStore(Temp.Combine("failed"), Notices);
        Capture = new CaptureCoordinator(Process, Device, Mic, tempFolder: TempFolder, failedStore: Failed);
        Workflow = new RecordingWorkflow(Capture, new RecordingProcessor(Whisper, Audio), new TranscriptSaver(), SettingsStore, Failed);
        Recording = new RecordingViewModel(Workflow, Dialogs, Messenger, Notices);
        Teams = new TargetItemViewModel(TargetApp.Teams);
        Recording.OnTargetSelected(Teams);
    }

    public TempDirectory Temp { get; } = new();

    public WeakReferenceMessenger Messenger { get; } = new();

    public UserNotices Notices { get; } = new();

    public FakeDialogs Dialogs { get; } = new();

    public FakeProcess Process { get; } = new();

    public FakeDevice Device { get; } = new();

    public FakeMic Mic { get; } = new();

    public FakeWhisper Whisper { get; } = new();

    public FakeAudio Audio { get; } = new();

    public FakeSummarizer Summarizer { get; } = new();

    public AppSettings Settings { get; }

    public FakeSettingsStore SettingsStore { get; }

    public string TempFolder { get; }

    public FailedRecordingStore Failed { get; }

    public CaptureCoordinator Capture { get; }

    public RecordingWorkflow Workflow { get; }

    public RecordingViewModel Recording { get; }

    public TargetItemViewModel Teams { get; }

    public void TeamsIsRunning(bool running) =>
        Teams.UpdateFrom(new TargetProcessStatus { App = Teams.App, IsRunning = running, ProcessId = running ? 4321u : 0u });

    public void WhisperSays(string text)
    {
        Whisper.ByFileName.Clear();
        var name = Path.GetFileNameWithoutExtension(Process.WavPath) + ".16k.wav";
        Whisper.ByFileName[name] = [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), text)];
    }

    public async Task StartRecordingAsync()
    {
        TeamsIsRunning(true);
        await Recording.ToggleRecordingCommand.ExecuteAsync(null);
    }

    public void LeaveOrphan(string baseName, string appName, string? sessionContent = null)
    {
        Directory.CreateDirectory(TempFolder);
        File.WriteAllText(Path.Combine(TempFolder, baseName + OrphanRecordingRecovery.SessionFileSuffix),
            sessionContent ?? JsonSerializer.Serialize(new CaptureSessionInfo { AppName = appName, RecordedAt = DateTime.Now }));
        WavFile.WriteTone(Path.Combine(TempFolder, baseName + ".wav"), 0.5, 0.5);
    }

    public ViewerViewModel CreateViewer() =>
        new(SettingsStore, Summarizer, new TranscriptHistoryService(), Dialogs, Messenger,
            new TranscriptRetranscriber(new RecordingProcessor(Whisper, Audio), SettingsStore));

    public void Dispose()
    {
        Recording.Dispose();
        Capture.Dispose();
        Messenger.Reset();
        Temp.Dispose();
    }
}
