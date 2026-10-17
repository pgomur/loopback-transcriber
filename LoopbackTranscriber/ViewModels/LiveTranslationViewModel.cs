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

public enum LiveTranslationState
{
    None,
    Pending,
    Done,
    Skipped,
}

public sealed class LiveLine : ObservableObject
{
    private string _translation = string.Empty;
    private LiveTranslationState _state;
    private bool _showOriginal;

    public LiveLine(int id, string original, DateTime at, LiveTranslationState state, bool showOriginal = true)
    {
        Id = id;
        Original = original;
        At = at;
        _state = state;
        _showOriginal = showOriginal;
    }

    public int Id { get; }

    public string Original { get; }

    public DateTime At { get; }

    private string _provisional = string.Empty;

    public string TranslationDisplay => _state == LiveTranslationState.Done ? _translation : _provisional;

    public bool ShowTranslation =>
        _state == LiveTranslationState.Done || (_state == LiveTranslationState.Pending && _provisional.Length > 0);

    public void SetProvisionalTranslation(string translation)
    {
        if (_state != LiveTranslationState.Pending || translation.Length == 0)
        {
            return;
        }

        _provisional = translation;
        NotifyState();
    }

    public bool ShowOriginalMain => !ShowTranslation;

    public bool ShowOriginalSmall => ShowTranslation && _showOriginal;

    private bool _isCurrent;
    private double _emphasis = 1.0;

    public bool IsCurrent => _isCurrent;

    public double Emphasis => _emphasis;

    public void SetEmphasis(bool isCurrent, double emphasis)
    {
        if (_isCurrent == isCurrent && Math.Abs(_emphasis - emphasis) < 0.001)
        {
            return;
        }

        _isCurrent = isCurrent;
        _emphasis = emphasis;
        OnPropertyChanged(nameof(IsCurrent));
        OnPropertyChanged(nameof(Emphasis));
    }

    public void SetShowOriginal(bool value)
    {
        if (_showOriginal == value)
        {
            return;
        }

        _showOriginal = value;
        OnPropertyChanged(nameof(ShowOriginalSmall));
    }

    public double MainOpacity => _state switch
    {
        LiveTranslationState.Pending => _provisional.Length > 0 ? 0.85 : 0.6,
        _ => 1.0,
    };

    public void SetTranslation(string translation)
    {
        _translation = translation;
        _state = LiveTranslationState.Done;
        NotifyState();
    }

    public void MarkSkipped()
    {
        if (_state != LiveTranslationState.Pending)
        {
            return;
        }

        _state = LiveTranslationState.Skipped;
        NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(TranslationDisplay));
        OnPropertyChanged(nameof(ShowTranslation));
        OnPropertyChanged(nameof(ShowOriginalMain));
        OnPropertyChanged(nameof(ShowOriginalSmall));
        OnPropertyChanged(nameof(MainOpacity));
    }
}

public enum LiveWindowState
{
    Idle,
    Recording,
    Busy,
    Finished,
}

public sealed class LiveTranslationViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly SettingsService _settingsService;
    private readonly Action _openMainWindow;
    private const double MinWindowOpacity = LiveTranslationPreferences.MinOpacity;

    private bool _isPinned = true;
    private double _windowOpacity;
    private string _selectedLanguageCode;
    private bool _wasBusy;
    private bool _finished;
    private string _finishedText = string.Empty;

    private const int MaxLines = 300;
    private LiveTranscriptionSession? _session;
    private bool _sessionAttempted;
    private readonly DispatcherTimer _signalTimer;
    private string _engineStatus = string.Empty;
    private bool _isTranscribingChunk;
    private string _draftText = string.Empty;
    private string _draftTranslationText = string.Empty;
    private LiveChunkMetrics? _lastMetrics;
    private LiveTranslationMetrics? _lastTranslation;
    private bool _translationAvailable;
    private string _translatorStatus = string.Empty;
    private string _metricsText = string.Empty;
    private bool _showOriginal;
    private bool _showMetricsSetting;
    private double _textSize;
    private bool _applyingSettings;

    public LiveTranslationViewModel(MainViewModel main, TargetItemViewModel target, SettingsService settingsService, Action openMainWindow)
    {
        _main = main;
        Target = target;
        _settingsService = settingsService;
        _openMainWindow = openMainWindow;
        _wasBusy = main.IsBusy;

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

        _signalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _signalTimer.Tick += (_, _) => UpdateMetricsText();

        _main.PropertyChanged += OnMainPropertyChanged;
        Target.PropertyChanged += OnTargetPropertyChanged;
        Strings.Instance.LanguageChanged += OnLanguageChanged;
        LiveTranslationPreferences.Changed += OnPreferencesChanged;

        UpdateSession();
    }

    public ObservableCollection<LiveLine> Lines { get; } = new();

    public bool ShowEmptyHint => Lines.Count == 0 && !ShowPending;

    public string EmptyHintText =>
        !IsRecordingThisApp ? Strings.Instance.Get("LiveWindowEmptyIdle")
        : string.IsNullOrEmpty(_engineStatus) ? Strings.Instance.Get("LiveWindowWaitingSpeech")
        : _engineStatus;

    public string DraftMainText
    {
        get
        {
            return LiveTextSplitter.ToLines(_translationAvailable ? _draftTranslationText : _draftText);
        }
    }

    public string DraftOriginalText =>
        _translationAvailable && _draftTranslationText.Length > 0 ? LiveTextSplitter.ToLines(_draftText) : string.Empty;

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

    private void UpdateLineEmphasis()
    {
        var draftShown = ShowPending;
        var count = Lines.Count;
        for (var age = 0; age < 6 && age < count; age++)
        {
            var distance = age + (draftShown ? 1 : 0);
            var emphasis = distance switch
            {
                0 => 1.0,
                1 => 0.75,
                2 => 0.6,
                _ => 0.45,
            };
            Lines[count - 1 - age].SetEmphasis(distance == 0, emphasis);
        }
    }

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

            foreach (var line in Lines)
            {
                line.SetShowOriginal(value);
            }

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

            if (_session is { } session)
            {
                session.TargetLanguageName = LanguageNameFor(value);
            }
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

    private bool IsRecordingThisApp => _main.IsRecording && ReferenceEquals(_main.SelectedTarget, Target);

    public LiveWindowState State =>
        IsRecordingThisApp ? LiveWindowState.Recording
        : _main.IsBusy ? LiveWindowState.Busy
        : _finished ? LiveWindowState.Finished
        : LiveWindowState.Idle;

    public string StatusLine => State switch
    {
        LiveWindowState.Recording => RecordingStatusLine(),
        LiveWindowState.Busy => _main.StatusText,
        LiveWindowState.Finished => _finishedText,
        _ => Strings.Instance.Get("LiveWindowIdle"),
    };

    private string RecordingStatusLine()
    {
        var text = $"{Strings.Instance.Get("LiveWindowRecording")} · {_main.ElapsedText}";
        if (!string.IsNullOrEmpty(_engineStatus))
        {
            text += " · " + _engineStatus;
        }

        if (!string.IsNullOrEmpty(_translatorStatus))
        {
            text += " · " + _translatorStatus;
        }

        return text;
    }

    public string ButtonText => IsRecordingThisApp
        ? Strings.Instance.Get("RecordStop")
        : Strings.Instance.Get("LiveWindowStart");

    public bool ShowProgress => _main.IsTranscribingActive;

    public double Progress => _main.TranscriptionProgressPercent;

    private bool CanStartOrStop => IsRecordingThisApp
        ? _main.CanToggleRecording
        : (State is LiveWindowState.Idle or LiveWindowState.Finished) && _main.CanStartRecordingFor(Target);

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

            return _main.RecordDisabledReason;
        }
    }

    private async Task StartOrStopAsync()
    {
        if (IsRecordingThisApp)
        {
            await _main.StopRecordingFromTrayAsync();
            return;
        }

        _finished = false;
        Refresh();
        await _main.StartRecordingForAsync(Target);
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RunOnUi(() =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsBusy))
            {
                if (_wasBusy && !_main.IsBusy && !_main.IsRecording)
                {
                    _finished = true;
                    _finishedText = _main.StatusText;
                }

                _wasBusy = _main.IsBusy;
            }
            else if (e.PropertyName == nameof(MainViewModel.IsRecording) && _main.IsRecording)
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
        var settings = _settingsService.Load();
        var modelPath = settings.GetModelFilePath();

        Lines.Clear();
        _lastMetrics = null;
        _lastTranslation = null;
        _isTranscribingChunk = false;
        _draftText = string.Empty;
        _draftTranslationText = string.Empty;
        _translatorStatus = string.Empty;
        _translationAvailable = false;
        NotifyDraftChanged();

        if (!File.Exists(modelPath))
        {
            _engineStatus = Strings.Instance.Get("LiveWindowModelMissing");
            return;
        }

        _engineStatus = Strings.Instance.Get("LiveWindowLoadingModel");

        LiveTranslatorConfig? translatorConfig = null;
        var translatorModelPath = settings.GetSummaryModelFilePath();
        if (File.Exists(translatorModelPath))
        {
            translatorConfig = new LiveTranslatorConfig
            {
                ModelPath = translatorModelPath,
                ThreadCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 6)
            };
            _translationAvailable = true;
            _translatorStatus = Strings.Instance.Get("LiveWindowLoadingTranslator");
        }
        else
        {
            _translatorStatus = Strings.Instance.Get("LiveWindowTranslationModelMissing");
        }

        var session = new LiveTranscriptionSession(new LiveWhisperConfig
        {
            ModelPath = modelPath,
            Language = string.IsNullOrWhiteSpace(settings.Language) ? "auto" : settings.Language,
            ThreadCount = WhisperTranscriptionService.GetRecommendedThreadCount(1),
            UseGpu = true,
            AudioContext = Math.Max(0, settings.LiveAudioContext),
            FlashAttention = settings.LiveFlashAttention
        }, translatorConfig, LanguageNameFor(_selectedLanguageCode));

        session.EngineReady += () => RunOnUi(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                _engineStatus = string.Empty;
                Refresh();
            }
        });

        session.LineReady += (id, text) => RunOnUi(() =>
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            var state = _translationAvailable ? LiveTranslationState.Pending : LiveTranslationState.None;
            var line = new LiveLine(id, text, DateTime.Now, state, _showOriginal);
            if (_translationAvailable && _draftTranslationText.Length > 0)
            {
                line.SetProvisionalTranslation(_draftTranslationText);
            }

            Lines.Add(line);

            _draftText = string.Empty;
            _draftTranslationText = string.Empty;
            NotifyDraftChanged();
            while (Lines.Count > MaxLines)
            {
                Lines.RemoveAt(0);
            }

            UpdateLineEmphasis();
        });

        session.ChunkProcessed += metrics => RunOnUi(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                _lastMetrics = metrics;
                UpdateMetricsText();
            }
        });

        session.TranscribingChanged += busy => RunOnUi(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                _isTranscribingChunk = busy;
                NotifyDraftChanged();
            }
        });

        session.TranslatorReady += () => RunOnUi(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                _translatorStatus = string.Empty;
                Refresh();
            }
        });

        session.DraftTranslationReady += translation => RunOnUi(() =>
        {
            if (ReferenceEquals(_session, session) && _draftText.Length > 0)
            {
                if (_draftTranslationText.Length > 20 && translation.Length < _draftTranslationText.Length * 0.6)
                {
                    return;
                }

                _draftTranslationText = translation;
                NotifyDraftChanged();
            }
        });

        session.TranslationReady += (id, translation) => RunOnUi(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                FindLine(id)?.SetTranslation(translation);
            }
        });

        session.TranslationSkipped += id => RunOnUi(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                FindLine(id)?.MarkSkipped();
            }
        });

        session.TranslationProcessed += metrics => RunOnUi(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                _lastTranslation = metrics;
                UpdateMetricsText();
            }
        });

        session.TranslationFailed += message => RunOnUi(() =>
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            _translationAvailable = false;
            _draftTranslationText = string.Empty;
            NotifyDraftChanged();
            _translatorStatus = Strings.Instance.Format("LiveWindowTranslationError", message);
            foreach (var line in Lines)
            {
                line.MarkSkipped();
            }

            Refresh();
        });

        session.DraftReady += text => RunOnUi(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                if (text.Length > 0 && _draftText.Length > 20 && text.Length < _draftText.Length * 0.6)
                {
                    return;
                }

                _draftText = text;
                if (text.Length == 0)
                {
                    _draftTranslationText = string.Empty;
                }

                NotifyDraftChanged();
            }
        });

        session.Failed += message => RunOnUi(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                _engineStatus = Strings.Instance.Format("LiveWindowEngineError", message);
                _isTranscribingChunk = false;
                _draftText = string.Empty;
                _draftTranslationText = string.Empty;
                NotifyDraftChanged();
                Refresh();
            }
        });

        _session = session;
        _main.LiveAudioCaptured += OnLiveAudioCaptured;
        session.Start();
        _signalTimer.Start();
        UpdateMetricsText();
    }

    private LiveLine? FindLine(int id)
    {
        for (var i = Lines.Count - 1; i >= 0; i--)
        {
            if (Lines[i].Id == id)
            {
                return Lines[i];
            }
        }

        return null;
    }

    private void StopSession()
    {
        _main.LiveAudioCaptured -= OnLiveAudioCaptured;
        _signalTimer.Stop();

        var session = _session;
        _session = null;
        session?.Dispose();

        _engineStatus = string.Empty;
        _translatorStatus = string.Empty;
        _isTranscribingChunk = false;
        _draftText = string.Empty;
        _draftTranslationText = string.Empty;
        _lastMetrics = null;
        _lastTranslation = null;
        NotifyDraftChanged();
        MetricsText = string.Empty;
    }

    private void OnLiveAudioCaptured(byte[] buffer, int count) => _session?.Feed(buffer, count);

    private void UpdateMetricsText()
    {
        var session = _session;
        if (session is null)
        {
            MetricsText = string.Empty;
            MetricsSignalText = string.Empty;
            MetricsChunkText = string.Empty;
            MetricsTranslationText = string.Empty;
            return;
        }

        var ci = CultureInfo.CurrentCulture;
        var level = session.LevelDb;
        var signal = level <= -75
            ? Strings.Instance.Get("LiveWindowNoSignal")
            : $"{level.ToString("0", ci)} dB";
        var signalText = Strings.Instance.Format("LiveWindowSignal", signal);

        var chunkText = string.Empty;
        if (_lastMetrics is { } m)
        {
            chunkText = Strings.Instance.Format(m.IsDraft ? "LiveWindowMetricsDraft" : "LiveWindowMetrics",
                m.AudioSeconds.ToString("0.0", ci),
                m.WhisperSeconds.ToString("0.0", ci),
                m.LatencySeconds.ToString("0.0", ci));
        }

        var translationText = string.Empty;
        if (_lastTranslation is { } translation)
        {
            translationText = Strings.Instance.Format("LiveWindowTranslationMetrics",
                translation.Seconds.ToString("0.0", ci),
                translation.LatencySeconds.ToString("0.0", ci));
        }

        MetricsSignalText = signalText;
        MetricsChunkText = chunkText;
        MetricsTranslationText = translationText;

        var text = signalText;
        if (chunkText.Length > 0)
        {
            text += " · " + chunkText;
        }

        if (translationText.Length > 0)
        {
            text += " · " + translationText;
        }

        MetricsText = text;
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
        _main.PropertyChanged -= OnMainPropertyChanged;
        Target.PropertyChanged -= OnTargetPropertyChanged;
        Strings.Instance.LanguageChanged -= OnLanguageChanged;
        LiveTranslationPreferences.Changed -= OnPreferencesChanged;
    }
}
