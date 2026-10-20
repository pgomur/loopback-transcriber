namespace LoopbackTranscriber.Services;

internal sealed record TranslatedDraft(string Original, string Translation);

internal sealed class LiveTranslatorStage : IDisposable
{
    private const int MaxQueuedTranslations = 6;

    private sealed record TranslationItem(int LineId, string Text, string? Context, string Target, DateTime CommittedAtUtc);

    private sealed record DraftTranslationItem(string Text, string? Context, string Target, int Generation, DateTime RequestedAtUtc);

    private readonly LiveWorkMailbox<TranslationItem, DraftTranslationItem> _mailbox;
    private readonly LiveGeneration _generation;
    private readonly LiveWorkerSupervisor _worker;
    private readonly Func<bool> _isSessionDisposed;
    private readonly List<LiveTranslationMetrics> _metrics = new();
    private volatile string _targetLanguageName;
    private TranslatedDraft? _lastTranslatedDraft;
    private string _lastRequestedDraftText = string.Empty;

    public LiveTranslatorStage(LiveTranslatorConfig config, LiveGeneration generation, string targetLanguageName,
        Func<bool> isSessionDisposed)
    {
        _generation = generation;
        _targetLanguageName = targetLanguageName;
        _isSessionDisposed = isSessionDisposed;
        _mailbox = new LiveWorkMailbox<TranslationItem, DraftTranslationItem>(MaxQueuedTranslations,
            item => TranslationSkipped?.Invoke(item.LineId));
        _worker = new LiveWorkerSupervisor(token => LiveWorkerClient.StartTranslatorAsync(config, token), isSessionDisposed,
            "translator",
            "LiveTranscriptionSession: the translator returned an error for a sentence; skipping it",
            "sentence translation error: ");
    }

    public event Action? TranslatorReady;

    public event Action<int, string>? TranslationReady;

    public event Action<int>? TranslationSkipped;

    public event Action<string>? DraftTranslationReady;

    public event Action<LiveTranslationMetrics>? TranslationProcessed;

    public event Action<UserMessage>? TranslationFailed;

    public string TargetLanguageName
    {
        get => _targetLanguageName;
        set => _targetLanguageName = value;
    }

    public int Dropped => _mailbox.Dropped;

    public TranslatedDraft? BeginCommit()
    {
        _mailbox.ClearDraft();
        _lastRequestedDraftText = string.Empty;
        return Interlocked.Exchange(ref _lastTranslatedDraft, null);
    }

    public void ReuseDraft(int lineId, string piece, TranslatedDraft cached)
    {
        LiveMetricsLog.Write("translation-reused chars=" + piece.Length);
        TranslationReady?.Invoke(lineId, cached.Translation);
    }

    public void EnqueueLine(int lineId, string piece, string? context) =>
        _mailbox.Post(new TranslationItem(lineId, piece, context, _targetLanguageName, DateTime.UtcNow));

    public void RequestDraft(string text, string? context, int generation)
    {
        if (text.Length == 0 || string.Equals(text, _lastRequestedDraftText, StringComparison.Ordinal))
        {
            return;
        }

        _lastRequestedDraftText = text;
        _mailbox.SetDraft(new DraftTranslationItem(text, context, _targetLanguageName, generation, DateTime.UtcNow));
    }

    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            await _worker.StartAsync(token).ConfigureAwait(false);

            TranslatorReady?.Invoke();

            while (!token.IsCancellationRequested)
            {
                DraftTranslationItem? draftItem = null;
                if (!_mailbox.TryTake(out var item))
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
                }

                var isDraft = draftItem is not null;
                var text = isDraft ? draftItem!.Text : item!.Text;
                var context = isDraft ? draftItem!.Context : item!.Context;
                var target = isDraft ? draftItem!.Target : item!.Target;
                var requestedAt = isDraft ? draftItem!.RequestedAtUtc : item!.CommittedAtUtc;

                var response = await _worker.SendAsync((client, t) => client.TranslateAsync(text, context, target, t), token)
                    .ConfigureAwait(false);
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
                    _mailbox.Count,
                    isDraft);

                lock (_metrics)
                {
                    _metrics.Add(metrics);
                }

                LiveMetricsLog.WriteTranslation(metrics);

                if (isDraft)
                {
                    if (translation.Length > 0 && draftItem!.Generation == _generation.Current)
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
            if (!_isSessionDisposed())
            {
                AppLog.Warn("LiveTranscriptionSession: live translation failed", ex);
                LiveMetricsLog.Write("translation error: " + ex.Message);
                TranslationFailed?.Invoke(UserMessage.From(ex));
            }
        }
        finally
        {
            _worker.Dispose();
        }
    }

    public void CompleteInput() => _mailbox.Complete();

    public void Dispose() => _worker.Dispose();

    public LiveTranslationMetrics[] SnapshotMetrics()
    {
        lock (_metrics)
        {
            return _metrics.ToArray();
        }
    }
}
