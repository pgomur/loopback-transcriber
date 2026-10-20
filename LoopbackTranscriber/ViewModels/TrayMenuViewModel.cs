using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public sealed record TrayMenuActions(
    Action RestoreWindow,
    Action<TargetItemViewModel> ToggleLiveTranslation,
    Action Exit,
    TargetItemViewModel? OpenLiveTranslationTarget);

public sealed class TrayMenuViewModel
{
    private readonly MainViewModel _shell;
    private readonly IRecordingHost _host;
    private readonly ISettingsStore _settings;

    public TrayMenuViewModel(MainViewModel shell, IRecordingHost host, ISettingsStore settings)
    {
        _shell = shell;
        _host = host;
        _settings = settings;

        RecordingActionCommand = CreateExclusiveCommand("recording");
        ExitCommand = CreateExclusiveCommand("exit");
    }

    public IAsyncRelayCommand<Func<Task>> RecordingActionCommand { get; }

    public IAsyncRelayCommand<Func<Task>> ExitCommand { get; }

    public bool TryRunExit(Func<Task> exit) => TryRun(ExitCommand, exit);

    public string Tooltip =>
        _shell.Recording.IsRecording && _shell.Targets.SelectedTarget is { } target
            ? Strings.Instance.Format("TrayTooltipRecording", target.DisplayName)
            : Strings.Instance.Get("TrayTooltipIdle");

    public bool TryConsumeTrayHint()
    {
        if (_settings.Snapshot().TrayHintShown)
        {
            return false;
        }

        _settings.Update(settings => settings.TrayHintShown = true);
        return true;
    }

    public IReadOnlyList<TrayMenuEntry> Build(TrayMenuActions actions)
    {
        var entries = new List<TrayMenuEntry>
        {
            new(Strings.Instance.Get("TrayOpen"), true, actions.RestoreWindow, Icon: "WindowNew20", IsDefault: true),
            TrayMenuEntry.Separator
        };

        if (!_shell.IsFirstRunActive)
        {
            var recording = _shell.Recording;

            if (recording.IsRecording)
            {
                var recordingName = _shell.Targets.SelectedTarget?.DisplayName ?? string.Empty;
                entries.Add(new(
                    Strings.Instance.Format("TrayRecordingStatus", recordingName),
                    false,
                    null,
                    IsStatus: true,
                    LiveText: () => recording.ElapsedText));
                entries.Add(new(
                    Strings.Instance.Format("TrayStopRecording", recordingName),
                    recording.CanToggleRecording && !RecordingActionCommand.IsRunning,
                    () => TryRun(RecordingActionCommand, () =>
                    {
                        actions.RestoreWindow();
                        return _host.StopRecordingAsync();
                    }),
                    Icon: TrayMenuEntry.StopGlyph));
            }
            else
            {
                foreach (var target in _shell.Targets.Items.ToList())
                {
                    var captured = target;
                    entries.Add(new(
                        Strings.Instance.Format("TrayStartRecording", captured.DisplayName),
                        _host.CanStartRecordingFor(captured) && !RecordingActionCommand.IsRunning,
                        () => TryRun(RecordingActionCommand, () => _host.StartRecordingForAsync(captured)),
                        Icon: TrayMenuEntry.RecordGlyph));
                }

                if (recording.RecordDisabledReason is { } reason)
                {
                    entries.Add(new(reason, false, null));
                }
            }

            if (actions.OpenLiveTranslationTarget is { } liveTarget)
            {
                entries.Add(new(
                    Strings.Instance.Format("TrayLiveTranslationClose", liveTarget.DisplayName),
                    true,
                    () => actions.ToggleLiveTranslation(liveTarget),
                    Icon: "Translate24"));
            }
            else
            {
                foreach (var target in _shell.Targets.Items.ToList())
                {
                    var captured = target;
                    entries.Add(new(
                        Strings.Instance.Format("TrayLiveTranslationOpen", captured.DisplayName),
                        captured.IsRunning,
                        () => actions.ToggleLiveTranslation(captured),
                        Icon: "Translate24"));
                }
            }

            entries.Add(TrayMenuEntry.Separator);
        }

        entries.Add(new(Strings.Instance.Get("TrayExit"), !ExitCommand.IsRunning, actions.Exit, Icon: "ArrowExit20"));
        return entries;
    }

    private static bool TryRun(IAsyncRelayCommand<Func<Task>> command, Func<Task> action)
    {
        if (!command.CanExecute(action))
        {
            AppLog.Info("TrayMenuViewModel: ignored a tray action because the previous one is still running");
            return false;
        }

        command.Execute(action);
        return true;
    }

    // Not bound from XAML: used as a "one action at a time" gate, for its CanExecute/IsRunning.
    private static AsyncRelayCommand<Func<Task>> CreateExclusiveCommand(string name) =>
        new(async action =>
        {
            try
            {
                await action!();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"TrayMenuViewModel: the tray {name} action failed", ex);
            }
        }, action => action is not null);
}
