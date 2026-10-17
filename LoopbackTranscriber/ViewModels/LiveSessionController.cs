using System.IO;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

internal sealed class LiveSessionController : IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly LiveTranscriptModel _model;
    private readonly Action<Action> _post;
    private LiveTranscriptionSession? _session;

    public LiveSessionController(ISettingsStore settings, LiveTranscriptModel model, Action<Action> post)
    {
        _settings = settings;
        _model = model;
        _post = post;
    }

    public string EngineStatus { get; private set; } = string.Empty;

    public string TranslatorStatus { get; private set; } = string.Empty;

    public LiveChunkMetrics? LastChunkMetrics { get; private set; }

    public LiveTranslationMetrics? LastTranslationMetrics { get; private set; }

    public bool IsActive => _session is not null;

    public double LevelDb => _session?.LevelDb ?? double.NegativeInfinity;

    public event Action? StateChanged;

    public event Action? DraftChanged;

    public event Action? MetricsChanged;

    public string TargetLanguageName
    {
        set
        {
            if (_session is { } session)
            {
                session.TargetLanguageName = value;
            }
        }
    }

    public bool Start(string targetLanguageName, Func<bool> showOriginal)
    {
        var settings = _settings.Load();
        var modelPath = settings.GetModelFilePath();

        _model.Reset();
        LastChunkMetrics = null;
        LastTranslationMetrics = null;
        TranslatorStatus = string.Empty;
        DraftChanged?.Invoke();

        if (!File.Exists(modelPath))
        {
            EngineStatus = Strings.Instance.Get("LiveWindowModelMissing");
            return false;
        }

        EngineStatus = Strings.Instance.Get("LiveWindowLoadingModel");

        LiveTranslatorConfig? translatorConfig = null;
        var translatorModelPath = settings.GetSummaryModelFilePath();
        if (File.Exists(translatorModelPath))
        {
            translatorConfig = new LiveTranslatorConfig
            {
                ModelPath = translatorModelPath,
                ThreadCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 6)
            };
            _model.TranslationAvailable = true;
            TranslatorStatus = Strings.Instance.Get("LiveWindowLoadingTranslator");
        }
        else
        {
            TranslatorStatus = Strings.Instance.Get("LiveWindowTranslationModelMissing");
        }

        var session = new LiveTranscriptionSession(new LiveWhisperConfig
        {
            ModelPath = modelPath,
            Language = string.IsNullOrWhiteSpace(settings.Language) ? "auto" : settings.Language,
            ThreadCount = WhisperTranscriptionService.GetRecommendedThreadCount(1),
            UseGpu = true,
            AudioContext = Math.Max(0, settings.LiveAudioContext),
            FlashAttention = settings.LiveFlashAttention
        }, translatorConfig, targetLanguageName);

        Wire(session, showOriginal);

        _session = session;
        session.Start();
        return true;
    }

    public void Feed(byte[] buffer, int count) => _session?.Feed(buffer, count);

    public void Stop()
    {
        var session = _session;
        _session = null;
        session?.Dispose();

        EngineStatus = string.Empty;
        TranslatorStatus = string.Empty;
        LastChunkMetrics = null;
        LastTranslationMetrics = null;
        _model.ClearDrafts();
    }

    public void Dispose() => Stop();

    private void Wire(LiveTranscriptionSession session, Func<bool> showOriginal)
    {
        void OnUi(Action action) => _post(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                action();
            }
        });

        session.EngineReady += () => OnUi(() =>
        {
            EngineStatus = string.Empty;
            StateChanged?.Invoke();
        });

        session.LineReady += (id, text) => OnUi(() =>
        {
            _model.AddLine(id, text, DateTime.Now, showOriginal());
            DraftChanged?.Invoke();
        });

        session.ChunkProcessed += metrics => OnUi(() =>
        {
            LastChunkMetrics = metrics;
            MetricsChanged?.Invoke();
        });

        session.TranscribingChanged += _ => OnUi(() => DraftChanged?.Invoke());

        session.TranslatorReady += () => OnUi(() =>
        {
            TranslatorStatus = string.Empty;
            StateChanged?.Invoke();
        });

        session.DraftTranslationReady += translation => OnUi(() =>
        {
            if (_model.SetDraftTranslation(translation))
            {
                DraftChanged?.Invoke();
            }
        });

        session.TranslationReady += (id, translation) => OnUi(() => _model.SetTranslation(id, translation));

        session.TranslationSkipped += id => OnUi(() => _model.MarkSkipped(id));

        session.TranslationProcessed += metrics => OnUi(() =>
        {
            LastTranslationMetrics = metrics;
            MetricsChanged?.Invoke();
        });

        session.TranslationFailed += message => OnUi(() =>
        {
            _model.TranslationFailed();
            TranslatorStatus = Strings.Instance.Format("LiveWindowTranslationError", message);
            DraftChanged?.Invoke();
            StateChanged?.Invoke();
        });

        session.DraftReady += text => OnUi(() =>
        {
            if (_model.SetDraft(text))
            {
                DraftChanged?.Invoke();
            }
        });

        session.Failed += message => OnUi(() =>
        {
            EngineStatus = Strings.Instance.Format("LiveWindowEngineError", message);
            _model.ClearDrafts();
            DraftChanged?.Invoke();
            StateChanged?.Invoke();
        });
    }
}
