using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public partial class LiveTranslationSettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsService;
    private readonly IMessenger _messenger;
    private bool _applying;

    public List<LanguageOption> AvailableLanguages { get; } =
    [
        new LanguageOption { Code = "es", DisplayName = "Español" },
        new LanguageOption { Code = "en", DisplayName = "English" },
        new LanguageOption { Code = "pt", DisplayName = "Português" },
        new LanguageOption { Code = "de", DisplayName = "Deutsch" },
        new LanguageOption { Code = "ru", DisplayName = "Русский" }
    ];

    [ObservableProperty]
    private string _liveTranslationLanguage;

    [ObservableProperty]
    private bool _liveShowOriginal;

    [ObservableProperty]
    private bool _liveShowMetrics;

    [ObservableProperty]
    private double _liveTextSize;

    [ObservableProperty]
    private double _liveWindowOpacity;

    public LiveTranslationSettingsViewModel(ISettingsStore settingsService, IMessenger? messenger = null)
    {
        _settingsService = settingsService;
        _messenger = messenger ?? WeakReferenceMessenger.Default;

        var settings = _settingsService.Snapshot();
        _liveTranslationLanguage = LiveTranslationPreferences.ResolveLanguage(settings, Strings.Instance.Language);
        _liveShowOriginal = settings.LiveShowOriginal;
        _liveShowMetrics = settings.LiveShowMetrics;
        _liveTextSize = LiveTranslationPreferences.ClampTextSize(settings.LiveTextSize);
        _liveWindowOpacity = LiveTranslationPreferences.ClampOpacity(settings.LiveWindowOpacity);

        _messenger.Register<LiveTranslationSettingsViewModel, LiveTranslationPreferencesChangedMessage>(this,
            static (recipient, _) => recipient.ApplyFromSettings());
        Strings.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(PreviewSignalText));
            OnPropertyChanged(nameof(PreviewChunkText));
            OnPropertyChanged(nameof(PreviewTranslationText));
        };
    }

    private static readonly Dictionary<string, string[]> PreviewSamples = new()
    {
        ["en"] = ["Great, so we agree to ship it on Thursday.", "Does anyone have questions before we wrap up…"],
        ["es"] = ["Genial, entonces quedamos en lanzarlo el jueves.", "¿Alguien tiene alguna pregunta antes de terminar…"],
        ["pt"] = ["Ótimo, então combinamos lançar na quinta-feira.", "Alguém tem alguma pergunta antes de terminarmos…"],
        ["de"] = ["Prima, dann einigen wir uns darauf, es am Donnerstag zu veröffentlichen.", "Hat jemand noch Fragen, bevor wir Schluss machen…"],
        ["ru"] = ["Отлично, тогда договариваемся выпустить это в четверг.", "Есть ли у кого-нибудь вопросы, прежде чем мы закончим…"],
    };

    private string[] TranslatedSample =>
        PreviewSamples.TryGetValue(LiveTranslationLanguage ?? string.Empty, out var sample) ? sample : PreviewSamples["en"];

    private string[] OriginalSample => PreviewSamples[string.Equals(LiveTranslationLanguage, "en", StringComparison.Ordinal) ? "es" : "en"];

    public string PreviewLineMain => TranslatedSample[0];

    public string PreviewLineOriginal => OriginalSample[0];

    public string PreviewDraftMain => TranslatedSample[1];

    public string PreviewDraftOriginal => OriginalSample[1];

    public bool PreviewShowOriginal => LiveShowOriginal;

    public double PreviewOriginalTextSize => Math.Max(11, Math.Round(LiveTextSize * 0.66, 1));

    public string PreviewSignalText => Strings.Instance.Format("LiveWindowSignal", "-28 dB");

    public bool HasPreviewChunkText => true;

    public bool HasPreviewTranslationText => true;

    public string PreviewChunkText
    {
        get
        {
            var ci = CultureInfo.CurrentCulture;
            return Strings.Instance.Format("LiveWindowMetricsDraft",
                (2.1).ToString("0.0", ci), (0.6).ToString("0.0", ci), (0.6).ToString("0.0", ci));
        }
    }

    public string PreviewTranslationText
    {
        get
        {
            var ci = CultureInfo.CurrentCulture;
            return Strings.Instance.Format("LiveWindowTranslationMetrics",
                (0.3).ToString("0.0", ci), (0.3).ToString("0.0", ci));
        }
    }

    private void NotifyPreviewTexts()
    {
        OnPropertyChanged(nameof(PreviewLineMain));
        OnPropertyChanged(nameof(PreviewLineOriginal));
        OnPropertyChanged(nameof(PreviewDraftMain));
        OnPropertyChanged(nameof(PreviewDraftOriginal));
        OnPropertyChanged(nameof(PreviewShowOriginal));
    }

    public void RefreshFromDisk() => ApplyFromSettings();

    partial void OnLiveTranslationLanguageChanged(string value)
    {
        NotifyPreviewTexts();
        Save();
    }

    partial void OnLiveShowOriginalChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewShowOriginal));
        Save();
    }

    partial void OnLiveShowMetricsChanged(bool value) => Save();

    partial void OnLiveTextSizeChanged(double value)
    {
        OnPropertyChanged(nameof(PreviewOriginalTextSize));
        Save();
    }

    partial void OnLiveWindowOpacityChanged(double value) => Save();

    private void Save()
    {
        if (_applying)
        {
            return;
        }

        var settings = _settingsService.Snapshot();
        var language = LiveTranslationLanguage ?? string.Empty;
        var textSize = LiveTranslationPreferences.ClampTextSize(LiveTextSize);
        var opacity = LiveTranslationPreferences.ClampOpacity(LiveWindowOpacity);

        if (string.Equals(settings.LiveTranslationLanguage, language, StringComparison.Ordinal)
            && settings.LiveShowOriginal == LiveShowOriginal
            && settings.LiveShowMetrics == LiveShowMetrics
            && Math.Abs(settings.LiveTextSize - textSize) < 0.0001
            && Math.Abs(settings.LiveWindowOpacity - opacity) < 0.0001)
        {
            return;
        }

        var showOriginal = LiveShowOriginal;
        var showMetrics = LiveShowMetrics;
        _settingsService.Update(updated =>
        {
            updated.LiveTranslationLanguage = language;
            updated.LiveShowOriginal = showOriginal;
            updated.LiveShowMetrics = showMetrics;
            updated.LiveTextSize = textSize;
            updated.LiveWindowOpacity = opacity;
        });

        _messenger.Send(new LiveTranslationPreferencesChangedMessage());
    }

    private void ApplyFromSettings()
    {
        var settings = _settingsService.Snapshot();

        _applying = true;
        try
        {
            LiveTranslationLanguage = LiveTranslationPreferences.ResolveLanguage(settings, Strings.Instance.Language);
            LiveShowOriginal = settings.LiveShowOriginal;
            LiveShowMetrics = settings.LiveShowMetrics;
            LiveTextSize = LiveTranslationPreferences.ClampTextSize(settings.LiveTextSize);
            LiveWindowOpacity = LiveTranslationPreferences.ClampOpacity(settings.LiveWindowOpacity);
        }
        finally
        {
            _applying = false;
        }
    }
}
