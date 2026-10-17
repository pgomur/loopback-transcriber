using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public enum LiveWindowState
{
    Idle,
    Recording,
    Busy,
    Finished,
}

public sealed class LiveTranslationViewModel : ObservableObject, IDisposable
{
    private readonly IRecordingHost _host;
    private readonly ISettingsStore _settingsService;
    private readonly Action _openMainWindow;
    private const double MinWindowOpacity = LiveTranslationPreferences.MinOpacity;

    private bool _isPinned = true;
    private double _windowOpacity;
    private string _selectedLanguageCode;
    private bool _wasBusy;
    private bool _finished;
    private string _finishedText = string.Empty;

    private readonly LiveTranscriptModel _model = new();
    private readonly LiveSessionController _controller;
    private bool _sessionAttempted;
    private readonly DispatcherTimer _signalTimer;
    private string _metricsText = string.Empty;
    private bool _showOriginal;
    private bool _showMetricsSetting;
    private double _textSize;
    private bool _applyingSettings;

    public LiveTranslationViewModel(IRecordingHost host, TargetItemViewModel target, ISettingsStore settingsService, Action openMainWindow)
    {
        _host = host;
        Target = target;
        _settingsService = settingsService;
        _openMainWindow = openMainWindow;
        _wasBusy = host.IsBusy;

        var initialSettings = settingsService.Load();
        var saved = initialSettings.LiveTranslationLanguage;
        _windowOpacity = Math.Clamp(initialSettings.LiveWindowOpacity, MinWindowOpacity, 1.0);
        _showOriginal = initialSettings.LiveShowOriginal;
        _showMetricsSetting = initialSettings.LiveShowMetrics;
        _textSize = LiveTranslationPreferences.ClampTextSize(initialSettings.LiveTextSize);
        _selectedLanguageCode =
            Languages.Any(l => l.Code == saved) ? saved
            : Languages.Any(l => l.Code == Strings.Instance.Language) ? Strings.Instance.Language
            : "en";

        StartStopCommand = new AsyncRelayCommand(StartOrStopAsync, () => CanStartOrStop);
        OpenMainWindowCommand = new RelayCommand(_openMainWindow);
        TogglePinCommand = new RelayCommand(() => IsPinned = !IsPinned);

        Lines.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowEmptyHint));
            OnPropertyChanged(nameof(EmptyHintText));
        };

        _controller = new LiveSessionController(settingsService, _model, RunOnUi);
        _controller.StateChanged += Refresh;
        _controller.DraftChanged += NotifyDraftChanged;
        _controller.MetricsChanged += UpdateMetricsText;

        _signalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _signalTimer.Tick += (_, _) => UpdateMetricsText();

        _host.PropertyChanged += OnMainPropertyChanged;
        Target.PropertyChanged += OnTargetPropertyChanged;
        Strings.Instance.LanguageChanged += OnLanguageChanged;
        LiveTranslationPreferences.Changed += OnPreferencesChanged;

        UpdateSession();
    }

    public ObservableCollection<LiveLine> Lines => _model.Lines;

    public bool ShowEmptyHint => Lines.Count == 0 && !ShowPending;

    public string EmptyHintText =>
        !IsRecordingThisApp ? Strings.Instance.Get("LiveWindowEmptyIdle")
        : string.IsNullOrEmpty(_controller.EngineStatus) ? Strings.Instance.Get("LiveWindowWaitingSpeech")
        : _controller.EngineStatus;

    public string DraftMainText => _model.DraftMainText;

    public string DraftOriginalText => _model.DraftOriginalText;

    public bool ShowDraftMain => DraftMainText.Length > 0;

    public bool ShowDraftOriginal => _showOriginal && DraftOriginalText.Length > 0;

    public bool ShowPending => ShowDraftMain || ShowDraftOriginal;

    private void NotifyDraftChanged()
    {
        OnPropertyChanged(nameof(DraftMainText));
        OnPropertyChanged(nameof(DraftOriginalText));
        OnPropertyChanged(nameof(ShowDraftMain));
        OnPropertyChanged(nameof(ShowDraftOriginal));
        OnPropertyChanged(nameof(ShowPending));
        OnPropertyChanged(nameof(ShowEmptyHint));
        OnPropertyChanged(nameof(EmptyHintText));
        UpdateLineEmphasis();
    }

    private void UpdateLineEmphasis() => _model.UpdateEmphasis(ShowPending);

    public string MetricsText
    {
        get => _metricsText;
        private set
        {
            if (SetProperty(ref _metricsText, value))
            {
                OnPropertyChanged(nameof(ShowMetrics));
            }
        }
    }

    public bool ShowMetrics => _showMetricsSetting && !string.IsNullOrEmpty(_metricsText);

    private string _metricsSignalText = string.Empty;
    private string _metricsChunkText = string.Empty;
    private string _metricsTranslationText = string.Empty;

    public string MetricsSignalText
    {
        get => _metricsSignalText;
        private set => SetProperty(ref _metricsSignalText, value);
    }

    public string MetricsChunkText
    {
        get => _metricsChunkText;
        private set
        {
            if (SetProperty(ref _metricsChunkText, value))
            {
                OnPropertyChanged(nameof(HasMetricsChunkText));
            }
        }
    }

    public string MetricsTranslationText
    {
        get => _metricsTranslationText;
        private set
        {
            if (SetProperty(ref _metricsTranslationText, value))
            {
                OnPropertyChanged(nameof(HasMetricsTranslationText));
            }
        }
    }

    public bool HasMetricsChunkText => _metricsChunkText.Length > 0;

    public bool HasMetricsTranslationText => _metricsTranslationText.Length > 0;

    public bool ShowOriginal
    {
        get => _showOriginal;
        private set
        {
            if (!SetProperty(ref _showOriginal, value))
            {
                return;
            }

            _model.SetShowOriginal(value);

            OnPropertyChanged(nameof(ShowDraftOriginal));
            OnPropertyChanged(nameof(ShowPending));
        }
    }

    public double TextSize
    {
        get => _textSize;
        private set
        {
            if (SetProperty(ref _textSize, value))
            {
                OnPropertyChanged(nameof(OriginalTextSize));
            }
        }
    }

    public double OriginalTextSize => Math.Max(11, Math.Round(_textSize * 0.66, 1));

    public TargetItemViewModel Target { get; }

    public string AppName => Target.DisplayName;

    public IAsyncRelayCommand StartStopCommand { get; }

    public IRelayCommand OpenMainWindowCommand { get; }

    public IRelayCommand TogglePinCommand { get; }

    public List<LanguageOption> Languages { get; } =
    [
        new LanguageOption { Code = "es", DisplayName = "Español" },
        new LanguageOption { Code = "en", DisplayName = "English" },
        new LanguageOption { Code = "pt", DisplayName = "Português" },
        new LanguageOption { Code = "de", DisplayName = "Deutsch" },
        new LanguageOption { Code = "ru", DisplayName = "Русский" },
    ];

    public string SelectedLanguageCode
    {
        get => _selectedLanguageCode;
        set
        {
            if (value is null || !SetProperty(ref _selectedLanguageCode, value))
            {
                return;
            }

            if (!_applyingSettings)
            {
                var settings = _settingsService.Load();
                if (!string.Equals(settings.LiveTranslationLanguage, value, StringComparison.Ordinal))
                {
                    settings.LiveTranslationLanguage = value;
                    _settingsService.Save(settings);
                    LiveTranslationPreferences.NotifyChanged();
                }
            }

            _controller.TargetLanguageName = LanguageNameFor(value);
        }
    }

    private static string LanguageNameFor(string code) => code switch
    {
        "es" => "Spanish",
        "pt" => "Portuguese",
        "de" => "German",
        "ru" => "Russian",
        _ => "English",
    };

    public double WindowOpacity
    {
        get => _windowOpacity;
        set => SetProperty(ref _windowOpacity, Math.Clamp(value, MinWindowOpacity, 1.0));
    }

    public void SaveWindowOpacity()
    {
        var settings = _settingsService.Load();
        if (Math.Abs(settings.LiveWindowOpacity - WindowOpacity) < 0.0001)
        {
            return;
        }

        settings.LiveWindowOpacity = WindowOpacity;
        _settingsService.Save(settings);
        LiveTranslationPreferences.NotifyChanged();
    }

    private void OnPreferencesChanged(object? sender, EventArgs e) => RunOnUi(ApplySettings);

    private void ApplySettings()
    {
        var settings = _settingsService.Load();

        _applyingSettings = true;
        try
        {
            SelectedLanguageCode = LiveTranslationPreferences.ResolveLanguage(settings);
        }
        finally
        {
            _applyingSettings = false;
        }

        WindowOpacity = settings.LiveWindowOpacity;
        ShowOriginal = settings.LiveShowOriginal;
        TextSize = LiveTranslationPreferences.ClampTextSize(settings.LiveTextSize);

        if (_showMetricsSetting != settings.LiveShowMetrics)
        {
            _showMetricsSetting = settings.LiveShowMetrics;
            OnPropertyChanged(nameof(ShowMetrics));
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(PinTooltip));
            }
        }
    }

    public string PinTooltip => Strings.Instance.Get(IsPinned ? "LiveWindowUnpinTooltip" : "LiveWindowPinTooltip");

    private bool IsRecordingThisApp => _host.IsRecording && ReferenceEquals(_host.SelectedTarget, Target);

    public LiveWindowState State =>
        IsRecordingThisApp ? LiveWindowState.Recording
        : _host.IsBusy ? LiveWindowState.Busy
        : _finished ? LiveWindowState.Finished
        : LiveWindowState.Idle;

    public string StatusLine => State switch
    {
        LiveWindowState.Recording => RecordingStatusLine(),
        LiveWindowState.Busy => _host.StatusText,
        LiveWindowState.Finished => _finishedText,
        _ => Strings.Instance.Get("LiveWindowIdle"),
    };

    private string RecordingStatusLine()
    {
        var text = $"{Strings.Instance.Get("LiveWindowRecording")} · {_host.ElapsedText}";
        if (!string.IsNullOrEmpty(_controller.EngineStatus))
        {
            text += " · " + _controller.EngineStatus;
        }

        if (!string.IsNullOrEmpty(_controller.TranslatorStatus))
        {
            text += " · " + _controller.TranslatorStatus;
        }

        return text;
    }

    public string ButtonText => IsRecordingThisApp
        ? Strings.Instance.Get("RecordStop")
        : Strings.Instance.Get("LiveWindowStart");

    public bool ShowProgress => _host.IsTranscribingActive;

    public double Progress => _host.TranscriptionProgressPercent;

    private bool CanStartOrStop => IsRecordingThisApp
        ? _host.CanToggleRecording
        : (State is LiveWindowState.Idle or LiveWindowState.Finished) && _host.CanStartRecordingFor(Target);

    public string? DisabledReason
    {
        get
        {
            if (CanStartOrStop || State == LiveWindowState.Busy)
            {
                return null;
            }

            if (!Target.IsRunning && !IsRecordingThisApp)
            {
                return Strings.Instance.Format("LiveWindowAppNotRunning", AppName);
            }

            return _host.RecordDisabledReason;
        }
    }

    private async Task StartOrStopAsync()
    {
        if (IsRecordingThisApp)
        {
            await _host.StopRecordingAsync();
            return;
        }

        _finished = false;
        Refresh();
        await _host.StartRecordingForAsync(Target);
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RunOnUi(() =>
        {
            if (e.PropertyName == nameof(IRecordingHost.IsBusy))
            {
                if (_wasBusy && !_host.IsBusy && !_host.IsRecording)
                {
                    _finished = true;
                    _finishedText = _host.StatusText;
                }

                _wasBusy = _host.IsBusy;
            }
            else if (e.PropertyName == nameof(IRecordingHost.IsRecording) && _host.IsRecording)
            {
                _finished = false;
            }

            Refresh();
        });
    }

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e) => RunOnUi(Refresh);

    private void OnLanguageChanged(object? sender, EventArgs e) => RunOnUi(Refresh);

    private void UpdateSession()
    {
        if (IsRecordingThisApp)
        {
            if (!_sessionAttempted)
            {
                _sessionAttempted = true;
                StartSession();
            }
        }
        else if (_sessionAttempted)
        {
            _sessionAttempted = false;
            StopSession();
        }
    }

    private void StartSession()
    {
        if (_controller.Start(LanguageNameFor(_selectedLanguageCode), () => _showOriginal))
        {
            _host.AudioCaptured += OnLiveAudioCaptured;
            _signalTimer.Start();
            UpdateMetricsText();
        }
    }

    private void StopSession()
    {
        _host.AudioCaptured -= OnLiveAudioCaptured;
        _signalTimer.Stop();
        _controller.Stop();
        NotifyDraftChanged();
        MetricsText = string.Empty;
    }

    private void OnLiveAudioCaptured(byte[] buffer, int count) => _controller.Feed(buffer, count);

    private void UpdateMetricsText()
    {
        if (!_controller.IsActive)
        {
            MetricsText = string.Empty;
            MetricsSignalText = string.Empty;
            MetricsChunkText = string.Empty;
            MetricsTranslationText = string.Empty;
            return;
        }

        var view = LiveMetricsPresenter.Format(_controller.LevelDb, _controller.LastChunkMetrics,
            _controller.LastTranslationMetrics, CultureInfo.CurrentCulture);

        MetricsSignalText = view.Signal;
        MetricsChunkText = view.Chunk;
        MetricsTranslationText = view.Translation;
        MetricsText = view.Combined;
    }

    private void Refresh()
    {
        UpdateSession();
        OnPropertyChanged(nameof(EmptyHintText));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(PinTooltip));
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(DisabledReason));
        StartStopCommand.NotifyCanExecuteChanged();
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    public void Dispose()
    {
        StopSession();
        _controller.Dispose();
        _host.PropertyChanged -= OnMainPropertyChanged;
        Target.PropertyChanged -= OnTargetPropertyChanged;
        Strings.Instance.LanguageChanged -= OnLanguageChanged;
        LiveTranslationPreferences.Changed -= OnPreferencesChanged;
    }
}
