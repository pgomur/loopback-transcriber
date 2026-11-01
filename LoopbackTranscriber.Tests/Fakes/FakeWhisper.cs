namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeWhisper : IWhisperTranscriber
{
    private readonly object _gate = new();
    private readonly List<TranscriptionRequest> _requests = [];

    public Dictionary<string, List<TranscriptSegment>> ByFileName { get; } = new();

    public Dictionary<string, string> DetectedLanguageByFileName { get; } = new();

    public bool Fail { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public List<TranscriptionRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToList();
            }
        }
    }

    public Task EnsureModelDownloadedAsync(string modelSize, string destinationPath, IProgress<UserMessage>? progress = null,
        IProgress<double>? percentProgress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<TrackTranscription> TranscribeAsync(TranscriptionRequest request, IProgress<int>? percentProgress = null,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _requests.Add(request);
        }

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        if (Fail)
        {
            throw new InvalidOperationException("fallo simulado del motor");
        }

        percentProgress?.Report(100);
        var key = Path.GetFileName(request.WavPath);
        var segments = ByFileName.TryGetValue(key, out var list) ? list : new List<TranscriptSegment>();
        var language = DetectedLanguageByFileName.TryGetValue(key, out var detected) ? detected : request.Language;
        return new TrackTranscription(segments, language);
    }
}
