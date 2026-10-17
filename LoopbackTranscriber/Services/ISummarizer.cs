using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public interface ISummarizer
{
    Task<RecordingSummary> SummarizeAsync(string transcriptText, string modelPath,
        IProgress<double>? percentProgress = null, CancellationToken cancellationToken = default);

    Task EnsureModelDownloadedAsync(string destinationPath,
        IProgress<string>? progress = null, IProgress<double>? percentProgress = null,
        CancellationToken cancellationToken = default);
}
