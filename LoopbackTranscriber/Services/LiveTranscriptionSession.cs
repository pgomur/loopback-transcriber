using System.Threading.Channels;
using System.Threading.Tasks;

namespace LoopbackTranscriber.Services;

internal sealed class LiveGeneration
{
    private int _value;

    public int Current => Volatile.Read(ref _value);

    public void Advance() => Interlocked.Increment(ref _value);
}

internal sealed class LiveTranscriptionSession : IDisposable
{
    private const int MaxQueuedAudioBlocks = 3000;

    private readonly LiveWhisperConfig _config;
    private readonly Channel<byte[]> _audio;
    private readonly CancellationTokenSource _cts = new();
    private readonly LiveAudioSegmenter _segmenter = new();
    private readonly LiveTranscriberStage _transcriber;
    private readonly LiveTranslatorStage? _translator;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private volatile string _targetLanguageName;
    private int _audioDropped;
    private int _disposed;

    public LiveTranscriptionSession(LiveWhisperConfig config, LiveTranslatorConfig? translatorConfig = null,
        string targetLanguageName = "English")
    {
        _config = config;
        _targetLanguageName = targetLanguageName;

        var generation = new LiveGeneration();
        Func<bool> isDisposed = () => Volatile.Read(ref _disposed) != 0;

        if (translatorConfig is not null)
        {
            _translator = new LiveTranslatorStage(translatorConfig, generation, targetLanguageName, isDisposed);
            _translator.TranslatorReady += () => TranslatorReady?.Invoke();
            _translator.TranslationReady += (lineId, text) => TranslationReady?.Invoke(lineId, text);
            _translator.TranslationSkipped += lineId => TranslationSkipped?.Invoke(lineId);
            _translator.DraftTranslationReady += text => DraftTranslationReady?.Invoke(text);
            _translator.TranslationProcessed += metrics => TranslationProcessed?.Invoke(metrics);
            _translator.TranslationFailed += message => TranslationFailed?.Invoke(message);
        }

        _transcriber = new LiveTranscriberStage(config, generation, _translator, isDisposed);
        _transcriber.EngineReady += () => EngineReady?.Invoke();
        _transcriber.LineReady += (lineId, text) => LineReady?.Invoke(lineId, text);
        _transcriber.DraftReady += text => DraftReady?.Invoke(text);
        _transcriber.TranscribingChanged += value => TranscribingChanged?.Invoke(value);
        _transcriber.ChunkProcessed += metrics => ChunkProcessed?.Invoke(metrics);
        _transcriber.Failed += message => Failed?.Invoke(message);

        _audio = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(MaxQueuedAudioBlocks)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            },
            _ =>
            {
                if (Interlocked.Increment(ref _audioDropped) == 1)
                {
                    AppLog.Info("LiveTranscriptionSession: audio queue is full; dropping the oldest audio");
                }
            });
    }

    public event Action? EngineReady;

    public event Action<int, string>? LineReady;

    public event Action? TranslatorReady;

    public event Action<int, string>? TranslationReady;

    public event Action<int>? TranslationSkipped;

    public event Action<string>? DraftTranslationReady;

    public event Action<LiveTranslationMetrics>? TranslationProcessed;

    public event Action<UserMessage>? TranslationFailed;

    public event Action<LiveChunkMetrics>? ChunkProcessed;

    public event Action<string>? DraftReady;

    public event Action<bool>? TranscribingChanged;

    public event Action<UserMessage>? Failed;

    public double LevelDb => _segmenter.LevelDb;

    public string TargetLanguageName
    {
        get => _targetLanguageName;
        set
        {
            _targetLanguageName = value;
            if (_translator is not null)
            {
                _translator.TargetLanguageName = value;
            }
        }
    }

    public void Start()
    {
        var token = _cts.Token;

        _ = Task.Run(SegmentLoopAsync);
        _ = Task.Run(() => _transcriber.RunAsync(token));

        if (_translator is not null)
        {
            _ = Task.Run(() => _translator.RunAsync(token));
        }
    }

    public void Feed(byte[] buffer, int count)
    {
        if (count <= 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var copy = new byte[count];
        Buffer.BlockCopy(buffer, 0, copy, 0, count);
        _audio.Writer.TryWrite(copy);
    }

    private async Task SegmentLoopAsync()
    {
        try
        {
            await foreach (var block in _audio.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                _segmenter.Feed(block, block.Length, _transcriber.OnChunk);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Warn("LiveTranscriptionSession: audio segmentation failed", ex);
            Failed?.Invoke(UserMessage.From(ex));
        }
        finally
        {
            _transcriber.CompleteInput();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch (Exception ex) when (ex is AggregateException or ObjectDisposedException)
        {
            AppLog.Warn("LiveTranscriptionSession: could not cancel the session", ex);
        }

        _audio.Writer.TryComplete();
        _transcriber.CompleteInput();
        _translator?.CompleteInput();

        _transcriber.Dispose();
        _translator?.Dispose();

        LiveSessionSummary.Write(_config, _startedAtUtc,
            _transcriber.SnapshotMetrics(), _transcriber.Dropped,
            _translator?.SnapshotMetrics() ?? Array.Empty<LiveTranslationMetrics>(), _translator?.Dropped ?? 0);
    }
}
