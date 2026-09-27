using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public partial class TargetItemViewModel : ObservableObject
{
    private readonly SettingsService? _settingsService;
    private bool _isLoadingSaveOptions;

    public TargetApp App { get; }

    public string DisplayName => App.DisplayName;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private uint _processId;

    [ObservableProperty]
    private bool _saveAudio = true;

    [ObservableProperty]
    private bool _saveTxt = true;

    [ObservableProperty]
    private bool _saveMarkdown = true;

    public bool CanToggleSaveAudio => IsRunning && !(SaveAudio && !SaveTxt && !SaveMarkdown);

    public bool CanToggleSaveTxt => IsRunning && !(SaveTxt && !SaveAudio && !SaveMarkdown);

    public bool CanToggleSaveMarkdown => IsRunning && !(SaveMarkdown && !SaveAudio && !SaveTxt);

    public TargetItemViewModel(TargetApp app, SettingsService? settingsService = null)
    {
        App = app;
        _settingsService = settingsService;

        if (_settingsService is not null)
        {
            _isLoadingSaveOptions = true;

            var settings = _settingsService.Load();
            var saveSettings = settings.GetAppSaveSettings(app.Id);
            SaveAudio = saveSettings.SaveAudio;
            SaveTxt = saveSettings.SaveTxt;
            SaveMarkdown = saveSettings.SaveMarkdown;

            _isLoadingSaveOptions = false;
        }

        NotifyToggleLocks();
    }

    public void UpdateFrom(TargetProcessStatus status)
    {
        IsRunning = status.IsRunning;
        ProcessId = status.ProcessId;
    }

    partial void OnIsRunningChanged(bool value) => NotifyToggleLocks();

    partial void OnSaveAudioChanged(bool value)
    {
        NotifyToggleLocks();
        PersistSaveOptions();
    }

    partial void OnSaveTxtChanged(bool value)
    {
        NotifyToggleLocks();
        PersistSaveOptions();
    }

    partial void OnSaveMarkdownChanged(bool value)
    {
        NotifyToggleLocks();
        PersistSaveOptions();
    }

    [RelayCommand(CanExecute = nameof(CanToggleSaveAudio))]
    private void ToggleSaveAudio() => SaveAudio = !SaveAudio;

    [RelayCommand(CanExecute = nameof(CanToggleSaveTxt))]
    private void ToggleSaveTxt() => SaveTxt = !SaveTxt;

    [RelayCommand(CanExecute = nameof(CanToggleSaveMarkdown))]
    private void ToggleSaveMarkdown() => SaveMarkdown = !SaveMarkdown;

    private void NotifyToggleLocks()
    {
        OnPropertyChanged(nameof(CanToggleSaveAudio));
        OnPropertyChanged(nameof(CanToggleSaveTxt));
        OnPropertyChanged(nameof(CanToggleSaveMarkdown));

        ToggleSaveAudioCommand.NotifyCanExecuteChanged();
        ToggleSaveTxtCommand.NotifyCanExecuteChanged();
        ToggleSaveMarkdownCommand.NotifyCanExecuteChanged();
    }

    private void PersistSaveOptions()
    {
        if (_isLoadingSaveOptions || _settingsService is null)
        {
            return;
        }

        var settings = _settingsService.Load();
        var saveSettings = settings.GetAppSaveSettings(App.Id);
        saveSettings.SaveAudio = SaveAudio;
        saveSettings.SaveTxt = SaveTxt;
        saveSettings.SaveMarkdown = SaveMarkdown;

        _settingsService.Save(settings);
    }
}
