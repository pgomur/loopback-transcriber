namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeSummarizer : ISummarizer
{
    public RecordingSummary Result { get; set; } = new() { Title = "Titulo generado", Summary = "Resumen generado" };

    public bool Fail { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public int Calls { get; private set; }

    public string? LastTranscript { get; private set; }

    public async Task<RecordingSummary> SummarizeAsync(string transcriptText, string modelPath, string language,
        IProgress<double>? percentProgress = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastTranscript = transcriptText;

        if (Gate is not null)
        {
            await Gate.Task;
        }

        if (Fail)
        {
            throw new InvalidOperationException("fallo simulado del resumen");
        }

        percentProgress?.Report(100);
        return Result;
    }

    public Task EnsureModelDownloadedAsync(string destinationPath, IProgress<UserMessage>? progress = null,
        IProgress<double>? percentProgress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
