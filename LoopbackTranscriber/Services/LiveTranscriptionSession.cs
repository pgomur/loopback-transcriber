using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LoopbackTranscriber.Services;

internal sealed record LiveChunkMetrics(
    double AudioSeconds,
    double WhisperSeconds,
    double LatencySeconds,
    int Backlog,
    int Dropped,
    int TextChars,
    bool IsDraft = false,
    bool Reused = false)
{
    public double RealTimeFactor => AudioSeconds <= 0 ? 0 : WhisperSeconds / AudioSeconds;
}

internal sealed record LiveTranslationMetrics(int Chars, double Seconds, double LatencySeconds, int Backlog, bool IsDraft = false);

internal sealed class LiveTranscriptionSession : IDisposable
{
    private const int MaxQueuedChunks = 8;
    private const int MaxQueuedTranslations = 6;

    private readonly LiveWhisperConfig _config;
    private const int MaxQueuedAudioBlocks = 3000;

    private readonly Channel<byte[]> _audio;
    private int _audioDropped;
    private readonly Channel<LiveAudioChunk> _chunks;
    private readonly CancellationTokenSource _cts = new();
    private readonly LiveAudioSegmenter _segmenter = new();
    private readonly List<LiveChunkMetrics> _allMetrics = new();
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;

    private sealed class DraftItem
    {
        public required LiveAudioChunk Chunk;
        public required int Generation;
    }

    private readonly SemaphoreSlim _wake = new(0, 1);
    private DraftItem? _draft;
    private int _finalCount;
    private int _lastDraftSeq;
    private string _lastDraftText = string.Empty;

    private sealed record TranslationItem(int LineId, string Text, string? Context, string Target, DateTime CommittedAtUtc);

    private sealed record DraftTranslationItem(string Text, string? Context, string Target, int Generation, DateTime RequestedAtUtc);

    private sealed record TranslatedDraft(string Original, string Translation);

    private readonly SemaphoreSlim _translatorWake = new(0, 1);
    private DraftTranslationItem? _draftTranslation;
    private TranslatedDraft? _lastTranslatedDraft;
    private string _lastRequestedDraftText = string.Empty;

    private readonly LiveTranslatorConfig? _translatorConfig;
    private readonly Channel<TranslationItem>? _translations;
    private readonly List<LiveTranslationMetrics> _translationMetrics = new();
    private volatile string _targetLanguageName;
    private int _lineCounter;
    private int _translationsDropped;
    private string? _previousLineText;
    private LiveWorkerClient? _translatorClient;

    private LiveWorkerClient? _client;
    private int _whisperRestarts;
    private int _translatorRestarts;

    private const int MaxWorkerRestarts = 3;
    private int _dropped;
    private int _disposed;
    private string _lastText = string.Empty;

    public LiveTranscriptionSession(LiveWhisperConfig config, LiveTranslatorConfig? translatorConfig = null,
        string targetLanguageName = "English")
    {
        _config = config;
        _translatorConfig = translatorConfig;
        _targetLanguageName = targetLanguageName;

        if (translatorConfig is not null)
        {
            _translations = Channel.CreateBounded<TranslationItem>(
                new BoundedChannelOptions(MaxQueuedTranslations)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true
                },
                item =>
                {
                    Interlocked.Increment(ref _translationsDropped);
                    TranslationSkipped?.Invoke(item.LineId);
                });
        }

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
                    AppLog.Info("LiveTranscriptionSession: la cola de audio esta llena; se descarta el audio mas antiguo");
                }
            });

        _chunks = Channel.CreateBounded<LiveAudioChunk>(
            new BoundedChannelOptions(MaxQueuedChunks)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            },
            _ => Interlocked.Increment(ref _dropped));
    }

    public event Action? EngineReady;

    public event Action<int, string>? LineReady;

    public event Action? TranslatorReady;

    public event Action<int, string>? TranslationReady;

    public event Action<int>? TranslationSkipped;

    public event Action<string>? DraftTranslationReady;

    public event Action<LiveTranslationMetrics>? TranslationProcessed;

    public event Action<string>? TranslationFailed;

    public event Action<LiveChunkMetrics>? ChunkProcessed;

    public event Action<string>? DraftReady;

    public event Action<bool>? TranscribingChanged;

    public event Action<string>? Failed;

    public double LevelDb => _segmenter.LevelDb;

    public string TargetLanguageName
    {
        get => _targetLanguageName;
        set => _targetLanguageName = value;
    }

    public void Start()
    {
        _ = Task.Run(SegmentLoopAsync);
        _ = Task.Run(TranscribeLoopAsync);

        if (_translations is not null)
        {
            _ = Task.Run(TranslateLoopAsync);
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
                _segmenter.Feed(block, block.Length, OnChunk);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Warn("LiveTranscriptionSession: fallo al segmentar el audio", ex);
            Failed?.Invoke(ex.Message);
        }
        finally
        {
            _chunks.Writer.TryComplete();
            Wake();
        }
    }

    private void OnChunk(LiveAudioChunk chunk)
    {
        if (chunk.IsDraft)
        {
            Interlocked.Exchange(ref _draft, new DraftItem { Chunk = chunk, Generation = Volatile.Read(ref _finalCount) });
        }
        else
        {
            Interlocked.Increment(ref _finalCount);

            var pending = Volatile.Read(ref _draft);
            if (pending is null || chunk.DraftSeq == 0 || pending.Chunk.DraftSeq != chunk.DraftSeq)
            {
                Interlocked.Exchange(ref _draft, null);
            }

            _chunks.Writer.TryWrite(chunk);
        }

        Wake();
    }

    private void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task TranscribeLoopAsync()
    {
        var token = _cts.Token;

        try
        {
            var whisperClient = await LiveWorkerClient.StartWhisperAsync(_config, token).ConfigureAwait(false);

            Interlocked.Exchange(ref _client, whisperClient);
            if (Volatile.Read(ref _disposed) != 0)
            {
                Interlocked.Exchange(ref _client, null)?.Dispose();
                return;
            }

            EngineReady?.Invoke();

            while (!token.IsCancellationRequested)
            {
                DraftItem? draftItem = null;
                LiveAudioChunk? chunk = null;

                var pendingDraft = Volatile.Read(ref _draft);
                var hasFinal = _chunks.Reader.TryPeek(out var nextFinal);
                var draftFirst = hasFinal && pendingDraft is not null
                                 && nextFinal!.DraftSeq > 0 && pendingDraft.Chunk.DraftSeq == nextFinal.DraftSeq;
                if (hasFinal && !draftFirst)
                {
                    _chunks.Reader.TryRead(out chunk);
                }

                if (chunk is null)
                {
                    draftItem = Interlocked.Exchange(ref _draft, null);
                    if (draftItem is null)
                    {
                        if (_chunks.Reader.Completion.IsCompleted)
                        {
                            break;
                        }

                        await _wake.WaitAsync(token).ConfigureAwait(false);
                        continue;
                    }

                    chunk = draftItem.Chunk;
                }

                var isDraft = draftItem is not null;
                if (!isDraft)
                {
                    TranscribingChanged?.Invoke(true);
                }

                var reused = !isDraft && chunk.DraftSeq > 0 && chunk.DraftSeq == _lastDraftSeq;
                string text;
                double whisperSeconds;
                if (reused)
                {
                    text = _lastDraftText;
                    whisperSeconds = 0;
                }
                else
                {
                    var started = DateTime.UtcNow;
                    var response = await TranscribeWithRecoveryAsync(chunk.Samples, token).ConfigureAwait(false);
                    if (response is null)
                    {
                        if (!isDraft && _chunks.Reader.Count == 0)
                        {
                            DraftReady?.Invoke(string.Empty);
                        }

                        TranscribingChanged?.Invoke(_chunks.Reader.Count > 0);
                        continue;
                    }

                    var elapsed = DateTime.UtcNow;

                    text = string.Join(' ', (response.Segments ?? new())
                        .Select(s => s.Text.Trim())
                        .Where(t => t.Length > 0));
                    whisperSeconds = response.Ms > 0 ? response.Ms / 1000.0 : (elapsed - started).TotalSeconds;
                }

                if (isDraft)
                {
                    _lastDraftSeq = chunk.DraftSeq;
                    _lastDraftText = text;
                }

                var finished = DateTime.UtcNow;
                var metrics = new LiveChunkMetrics(
                    chunk.DurationSeconds,
                    whisperSeconds,
                    Math.Max(0, (finished - chunk.SpeechEndedAtUtc).TotalSeconds),
                    _chunks.Reader.Count,
                    Volatile.Read(ref _dropped),
                    text.Length,
                    isDraft,
                    reused);

                lock (_allMetrics)
                {
                    _allMetrics.Add(metrics);
                }

                LiveMetricsLog.WriteChunk(metrics);

                if (isDraft)
                {
                    if (draftItem!.Generation == Volatile.Read(ref _finalCount) && text.Length > 0)
                    {
                        DraftReady?.Invoke(text);
                        RequestDraftTranslation(text, draftItem.Generation);
                    }
                }
                else
                {
                    if (text.Length > 0 && !string.Equals(text, _lastText, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastText = text;
                        CommitLine(text);
                    }

                    if (Volatile.Read(ref _finalCount) == 0 || _chunks.Reader.Count == 0)
                    {
                        DraftReady?.Invoke(string.Empty);
                    }

                    TranscribingChanged?.Invoke(_chunks.Reader.Count > 0);
                }

                ChunkProcessed?.Invoke(metrics);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                AppLog.Warn("LiveTranscriptionSession: fallo en la transcripcion en vivo", ex);
                LiveMetricsLog.Write("error: " + ex.Message);
                Failed?.Invoke(ex.Message);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _client, null)?.Dispose();
        }
    }

    private async Task<LiveWorkerMessage?> TranscribeWithRecoveryAsync(short[] samples, CancellationToken token)
    {
        try
        {
            var client = Volatile.Read(ref _client) ?? throw new OperationCanceledException(token);
            return await client.TranscribeAsync(samples, token).ConfigureAwait(false);
        }
        catch (LiveWorkerRequestException ex) when (!token.IsCancellationRequested)
        {
            AppLog.Warn("LiveTranscriptionSession: Whisper devolvio un error para un trozo, se omite", ex);
            LiveMetricsLog.Write("error de trozo: " + ex.Message);
            return null;
        }
        catch (Exception ex) when (IsRecoverable(ex, token) && _whisperRestarts < MaxWorkerRestarts)
        {
            _whisperRestarts++;
            AppLog.Warn($"LiveTranscriptionSession: se reinicia el proceso de Whisper ({_whisperRestarts}/{MaxWorkerRestarts})", ex);
            LiveMetricsLog.Write($"reinicio de Whisper ({_whisperRestarts}/{MaxWorkerRestarts}): " + ex.Message);

            Interlocked.Exchange(ref _client, null)?.Dispose();
            var restarted = await LiveWorkerClient.StartWhisperAsync(_config, token).ConfigureAwait(false);
            Interlocked.Exchange(ref _client, restarted);
            if (Volatile.Read(ref _disposed) != 0)
            {
                Interlocked.Exchange(ref _client, null)?.Dispose();
                throw new OperationCanceledException(token);
            }

            return null;
        }
    }

    private async Task<LiveWorkerMessage?> TranslateWithRecoveryAsync(string text, string? context, string target, CancellationToken token)
    {
        try
        {
            var client = Volatile.Read(ref _translatorClient) ?? throw new OperationCanceledException(token);
            return await client.TranslateAsync(text, context, target, token).ConfigureAwait(false);
        }
        catch (LiveWorkerRequestException ex) when (!token.IsCancellationRequested)
        {
            AppLog.Warn("LiveTranscriptionSession: el traductor devolvio un error para una frase, se omite", ex);
            LiveMetricsLog.Write("error de traduccion de frase: " + ex.Message);
            return null;
        }
        catch (Exception ex) when (IsRecoverable(ex, token) && _translatorRestarts < MaxWorkerRestarts)
        {
            _translatorRestarts++;
            AppLog.Warn($"LiveTranscriptionSession: se reinicia el proceso del traductor ({_translatorRestarts}/{MaxWorkerRestarts})", ex);
            LiveMetricsLog.Write($"reinicio del traductor ({_translatorRestarts}/{MaxWorkerRestarts}): " + ex.Message);

            Interlocked.Exchange(ref _translatorClient, null)?.Dispose();
            var restarted = await LiveWorkerClient.StartTranslatorAsync(_translatorConfig!, token).ConfigureAwait(false);
            Interlocked.Exchange(ref _translatorClient, restarted);
            if (Volatile.Read(ref _disposed) != 0)
            {
                Interlocked.Exchange(ref _translatorClient, null)?.Dispose();
                throw new OperationCanceledException(token);
            }

            return null;
        }
    }

    private bool IsRecoverable(Exception ex, CancellationToken token) =>
        !token.IsCancellationRequested
        && Volatile.Read(ref _disposed) == 0
        && ex is TimeoutException or InvalidOperationException;

    private void CommitLine(string text)
    {
        var pieces = LiveTextSplitter.Split(text);
        if (pieces.Count == 0)
        {
            return;
        }

        TranslatedDraft? cached = null;
        if (_translations is not null)
        {
            Interlocked.Exchange(ref _draftTranslation, null);
            _lastRequestedDraftText = string.Empty;
            cached = Interlocked.Exchange(ref _lastTranslatedDraft, null);
        }

        foreach (var piece in pieces)
        {
            var lineId = Interlocked.Increment(ref _lineCounter);
            var context = _previousLineText;
            _previousLineText = piece;

            LineReady?.Invoke(lineId, piece);

            if (_translations is null)
            {
                continue;
            }

            if (pieces.Count == 1 && cached is not null
                && string.Equals(cached.Original, piece, StringComparison.OrdinalIgnoreCase))
            {
                LiveMetricsLog.Write("traduccion-reutiliza caracteres=" + piece.Length);
                TranslationReady?.Invoke(lineId, cached.Translation);
                continue;
            }

            _translations.Writer.TryWrite(new TranslationItem(lineId, piece, context, _targetLanguageName, DateTime.UtcNow));
            WakeTranslator();
        }
    }

    private void RequestDraftTranslation(string text, int generation)
    {
        if (_translations is null || text.Length == 0 || string.Equals(text, _lastRequestedDraftText, StringComparison.Ordinal))
        {
            return;
        }

        _lastRequestedDraftText = text;
        Interlocked.Exchange(ref _draftTranslation,
            new DraftTranslationItem(text, _previousLineText, _targetLanguageName, generation, DateTime.UtcNow));
        WakeTranslator();
    }

    private void WakeTranslator()
    {
        try
        {
            _translatorWake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task TranslateLoopAsync()
    {
        var token = _cts.Token;

        try
        {
            var translatorClient = await LiveWorkerClient.StartTranslatorAsync(_translatorConfig!, token).ConfigureAwait(false);
            Interlocked.Exchange(ref _translatorClient, translatorClient);
            if (Volatile.Read(ref _disposed) != 0)
            {
                Interlocked.Exchange(ref _translatorClient, null)?.Dispose();
                return;
            }

            TranslatorReady?.Invoke();

            while (!token.IsCancellationRequested)
            {
                DraftTranslationItem? draftItem = null;
                if (!_translations!.Reader.TryRead(out var item))
                {
                    draftItem = Interlocked.Exchange(ref _draftTranslation, null);
                    if (draftItem is null)
                    {
                        if (_translations.Reader.Completion.IsCompleted)
                        {
                            break;
                        }

                        await _translatorWake.WaitAsync(token).ConfigureAwait(false);
                        continue;
                    }
                }

                var isDraft = draftItem is not null;
                var text = isDraft ? draftItem!.Text : item!.Text;
                var context = isDraft ? draftItem!.Context : item!.Context;
                var target = isDraft ? draftItem!.Target : item!.Target;
                var requestedAt = isDraft ? draftItem!.RequestedAtUtc : item!.CommittedAtUtc;

                var response = await TranslateWithRecoveryAsync(text, context, target, token).ConfigureAwait(false);
                if (response is null)
                {
                    if (!isDraft)
                    {
                        TranslationSkipped?.Invoke(item!.LineId);
                    }

                    continue;
                }

                var translation = response.Text?.Trim() ?? string.Empty;
                var metrics = new LiveTranslationMetrics(
                    text.Length,
                    response.Ms / 1000.0,
                    Math.Max(0, (DateTime.UtcNow - requestedAt).TotalSeconds),
                    _translations.Reader.Count,
                    isDraft);

                lock (_translationMetrics)
                {
                    _translationMetrics.Add(metrics);
                }

                LiveMetricsLog.WriteTranslation(metrics);

                if (isDraft)
                {
                    if (translation.Length > 0 && draftItem!.Generation == Volatile.Read(ref _finalCount))
                    {
                        Interlocked.Exchange(ref _lastTranslatedDraft, new TranslatedDraft(text, translation));
                        DraftTranslationReady?.Invoke(translation);
                    }
                }
                else if (translation.Length > 0)
                {
                    TranslationReady?.Invoke(item!.LineId, translation);
                }
                else
                {
                    TranslationSkipped?.Invoke(item!.LineId);
                }

                TranslationProcessed?.Invoke(metrics);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                AppLog.Warn("LiveTranscriptionSession: fallo en la traduccion en vivo", ex);
                LiveMetricsLog.Write("error de traduccion: " + ex.Message);
                TranslationFailed?.Invoke(ex.Message);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _translatorClient, null)?.Dispose();
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
        catch (Exception ex)
        {
            AppLog.Warn("LiveTranscriptionSession: no se pudo cancelar la sesion", ex);
        }

        _audio.Writer.TryComplete();
        _chunks.Writer.TryComplete();
        _translations?.Writer.TryComplete();
        WakeTranslator();

        Interlocked.Exchange(ref _client, null)?.Dispose();
        Interlocked.Exchange(ref _translatorClient, null)?.Dispose();

        WriteSummary();
    }

    private void WriteSummary()
    {
        LiveChunkMetrics[] all;
        lock (_allMetrics)
        {
            all = _allMetrics.ToArray();
        }

        var finals = all.Where(m => !m.IsDraft).ToArray();
        var drafts = all.Where(m => m.IsDraft).ToArray();
        if (all.Length == 0)
        {
            LiveMetricsLog.Write($"sesion sin trozos transcritos ({(DateTime.UtcNow - _startedAtUtc).TotalSeconds:0} s)");
            return;
        }

        var model = Path.GetFileName(_config.ModelPath);
        var dropped = Volatile.Read(ref _dropped);

        var reused = finals.Count(m => m.Reused);
        var line = FormattableString.Invariant(
            $"RESUMEN modelo={model} gpu={_config.UseGpu} ctx={_config.AudioContext} flash={_config.FlashAttention} ");
        line += FormattableString.Invariant($"descartados={dropped} reutilizados={reused}");
        line += Describe("trozos", finals);
        line += Describe("borradores", drafts);
        line += DescribeTranslations();
        LiveMetricsLog.Write(line);
    }

    private string DescribeTranslations()
    {
        LiveTranslationMetrics[] items;
        lock (_translationMetrics)
        {
            items = _translationMetrics.ToArray();
        }

        return DescribeTranslationSeries("traducciones", items.Where(m => !m.IsDraft).ToArray())
               + DescribeTranslationSeries("traducciones_borrador", items.Where(m => m.IsDraft).ToArray())
               + (items.Length > 0
                   ? FormattableString.Invariant($" descartadas={Volatile.Read(ref _translationsDropped)}")
                   : string.Empty);
    }

    private static string DescribeTranslationSeries(string name, LiveTranslationMetrics[] items)
    {
        if (items.Length == 0)
        {
            return string.Empty;
        }

        var model = Series(items.Select(m => m.Seconds));
        var latency = Series(items.Select(m => m.LatencySeconds));

        return FormattableString.Invariant(
            $" | {name}={items.Length} modelo_p50={model.P50:0.0}s p90={model.P90:0.0}s retraso_p50={latency.P50:0.0}s p90={latency.P90:0.0}s");
    }

    private static (double P50, double P90) Series(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return (sorted[sorted.Length / 2], sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.9))]);
    }

    private static string Describe(string name, LiveChunkMetrics[] items)
    {
        if (items.Length == 0)
        {
            return string.Empty;
        }

        var audio = items.Sum(m => m.AudioSeconds);
        var whisper = items.Sum(m => m.WhisperSeconds);
        var rtf = audio > 0 ? whisper / audio : 0;
        var latencies = items.Select(m => m.LatencySeconds).OrderBy(v => v).ToArray();
        var p50 = latencies[latencies.Length / 2];
        var p90 = latencies[Math.Min(latencies.Length - 1, (int)(latencies.Length * 0.9))];
        var max = latencies[^1];

        return FormattableString.Invariant(
            $" | {name}={items.Length} audio={audio:0.0}s whisper={whisper:0.0}s rtf={rtf:0.00} retraso_p50={p50:0.0}s p90={p90:0.0}s max={max:0.0}s");
    }
}

internal static class LiveMetricsLog
{
    private static readonly object Gate = new();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "live-metrics.log");

    public static void WriteChunk(LiveChunkMetrics m)
    {
        var label = m.IsDraft ? "borrador" : m.Reused ? "trozo-reutiliza" : "trozo";
        var first = FormattableString.Invariant(
            $"{label} audio={m.AudioSeconds:0.0}s whisper={m.WhisperSeconds:0.00}s rtf={m.RealTimeFactor:0.00} ");
        var second = FormattableString.Invariant(
            $"retraso={m.LatencySeconds:0.0}s cola={m.Backlog} descartados={m.Dropped} caracteres={m.TextChars}");
        Write(first + second);
    }

    public static void WriteTranslation(LiveTranslationMetrics m)
    {
        var label = m.IsDraft ? "traduccion-borrador" : "traduccion";
        var first = FormattableString.Invariant(
            $"{label} caracteres={m.Chars} modelo={m.Seconds:0.00}s ");
        var second = FormattableString.Invariant($"retraso={m.LatencySeconds:0.0}s cola={m.Backlog}");
        Write(first + second);
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                {
                    File.Delete(path);
                }

                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("LiveMetricsLog: no se pudo escribir el registro", ex);
        }
    }
}
