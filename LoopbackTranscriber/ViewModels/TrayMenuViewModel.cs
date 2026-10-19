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
    }

    public string Tooltip =>
        _shell.Recording.IsRecording && _shell.Targets.SelectedTarget is { } target
            ? Strings.Instance.Format("TrayTooltipRecording", target.DisplayName)
            : Strings.Instance.Get("TrayTooltipIdle");

    public bool TryConsumeTrayHint()
    {
        var settings = _settings.Load();
        if (settings.TrayHintShown)
        {
            return false;
        }

        settings.TrayHintShown = true;
        _settings.Save(settings);
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
                    recording.CanToggleRecording,
                    () =>
                    {
                        actions.RestoreWindow();
                        RunAction(_host.StopRecordingAsync);
                    },
                    Icon: TrayMenuEntry.StopGlyph));
            }
            else
            {
                foreach (var target in _shell.Targets.Items.ToList())
                {
                    var captured = target;
                    entries.Add(new(
                        Strings.Instance.Format("TrayStartRecording", captured.DisplayName),
                        _host.CanStartRecordingFor(captured),
                        () => RunAction(() => _host.StartRecordingForAsync(captured)),
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

        entries.Add(new(Strings.Instance.Get("TrayExit"), true, actions.Exit, Icon: "ArrowExit20"));
        return entries;
    }

    public static async void RunAction(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppLog.Warn("TrayMenuViewModel.RunAction: tray action failed", ex);
        }
    }
}
