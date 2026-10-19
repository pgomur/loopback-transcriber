namespace LoopbackTranscriber.Services;

internal sealed class LiveTranscriberStage
{
    private const int MaxQueuedChunks = 8;

    private sealed class DraftItem
    {
        public required LiveAudioChunk Chunk;
        public required int Generation;
    }

    private readonly LiveWorkMailbox<LiveAudioChunk, DraftItem> _mailbox = new(MaxQueuedChunks);
    private readonly LiveGeneration _generation;
    private readonly LiveWorkerSupervisor _worker;
    private readonly LiveTranslatorStage? _translator;
    private readonly Func<bool> _isSessionDisposed;
    private readonly List<LiveChunkMetrics> _metrics = new();
    private int _lastDraftSeq;
    private string _lastDraftText = string.Empty;
    private string _lastText = string.Empty;
    private int _lineCounter;
    private string? _previousLineText;

    public LiveTranscriberStage(LiveWhisperConfig config, LiveGeneration generation, LiveTranslatorStage? translator,
        Func<bool> isSessionDisposed)
    {
        _generation = generation;
        _translator = translator;
        _isSessionDisposed = isSessionDisposed;
        _worker = new LiveWorkerSupervisor(token => LiveWorkerClient.StartWhisperAsync(config, token), isSessionDisposed,
            "Whisper",
            "LiveTranscriptionSession: Whisper returned an error for a chunk; skipping it",
            "chunk error: ");
    }

    public event Action? EngineReady;

    public event Action<int, string>? LineReady;

    public event Action<string>? DraftReady;

    public event Action<bool>? TranscribingChanged;

    public event Action<LiveChunkMetrics>? ChunkProcessed;

    public event Action<UserMessage>? Failed;

    public int Dropped => _mailbox.Dropped;

    public void OnChunk(LiveAudioChunk chunk)
    {
        if (chunk.IsDraft)
        {
            _mailbox.SetDraft(new DraftItem { Chunk = chunk, Generation = _generation.Current });
            return;
        }

        _generation.Advance();

        var pending = _mailbox.PeekDraft();
        if (pending is null || chunk.DraftSeq == 0 || pending.Chunk.DraftSeq != chunk.DraftSeq)
        {
            _mailbox.ClearDraft();
        }

        _mailbox.Post(chunk);
    }

    public void CompleteInput() => _mailbox.Complete();

    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            await _worker.StartAsync(token).ConfigureAwait(false);

            EngineReady?.Invoke();

            while (!token.IsCancellationRequested)
            {
                DraftItem? draftItem = null;
                LiveAudioChunk? chunk = null;

                var pendingDraft = _mailbox.PeekDraft();
                var hasFinal = _mailbox.TryPeek(out var nextFinal);
                var draftFirst = hasFinal && pendingDraft is not null
                                 && nextFinal!.DraftSeq > 0 && pendingDraft.Chunk.DraftSeq == nextFinal.DraftSeq;
                if (hasFinal && !draftFirst)
                {
                    _mailbox.TryTake(out chunk);
                }

                if (chunk is null)
                {
                    draftItem = _mailbox.TakeDraft();
                    if (draftItem is null)
                    {
                        if (_mailbox.IsCompleted)
                        {
                            break;
                        }

                        await _mailbox.WaitAsync(token).ConfigureAwait(false);
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
                    var samples = chunk.Samples;
                    var started = DateTime.UtcNow;
                    var response = await _worker.SendAsync((client, t) => client.TranscribeAsync(samples, t), token)
                        .ConfigureAwait(false);
                    if (response is null)
                    {
                        if (!isDraft && _mailbox.Count == 0)
                        {
                            DraftReady?.Invoke(string.Empty);
                        }

                        TranscribingChanged?.Invoke(_mailbox.Count > 0);
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
                    _mailbox.Count,
                    _mailbox.Dropped,
                    text.Length,
                    isDraft,
                    reused);

                lock (_metrics)
                {
                    _metrics.Add(metrics);
                }

                LiveMetricsLog.WriteChunk(metrics);

                if (isDraft)
                {
                    if (draftItem!.Generation == _generation.Current && text.Length > 0)
                    {
                        DraftReady?.Invoke(text);
                        _translator?.RequestDraft(text, _previousLineText, draftItem.Generation);
                    }
                }
                else
                {
                    if (text.Length > 0 && !string.Equals(text, _lastText, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastText = text;
                        CommitLine(text);
                    }

                    if (_generation.Current == 0 || _mailbox.Count == 0)
                    {
                        DraftReady?.Invoke(string.Empty);
                    }

                    TranscribingChanged?.Invoke(_mailbox.Count > 0);
                }

                ChunkProcessed?.Invoke(metrics);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isSessionDisposed())
            {
                AppLog.Warn("LiveTranscriptionSession: live transcription failed", ex);
                LiveMetricsLog.Write("error: " + ex.Message);
                Failed?.Invoke(UserMessage.From(ex));
            }
        }
        finally
        {
            _worker.Dispose();
        }
    }

    private void CommitLine(string text)
    {
        var pieces = LiveTextSplitter.Split(text);
        if (pieces.Count == 0)
        {
            return;
        }

        var cached = _translator?.BeginCommit();

        foreach (var piece in pieces)
        {
            var lineId = ++_lineCounter;
            var context = _previousLineText;
            _previousLineText = piece;

            LineReady?.Invoke(lineId, piece);

            if (_translator is null)
            {
                continue;
            }

            if (pieces.Count == 1 && cached is not null
                && string.Equals(cached.Original, piece, StringComparison.OrdinalIgnoreCase))
            {
                _translator.ReuseDraft(lineId, piece, cached);
                continue;
            }

            _translator.EnqueueLine(lineId, piece, context);
        }
    }

    public void DisposeWorker() => _worker.Dispose();

    public LiveChunkMetrics[] SnapshotMetrics()
    {
        lock (_metrics)
        {
            return _metrics.ToArray();
        }
    }
}
