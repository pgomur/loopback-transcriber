using System.IO;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed record ModelVerificationResult(bool TranscriptionModelRemoved, bool SummaryModelRemoved)
{
    public bool AnyRemoved => TranscriptionModelRemoved || SummaryModelRemoved;
}

public interface IModelVerifier
{
    Task<ModelVerificationResult> VerifyInstalledModelsAsync(CancellationToken cancellationToken = default);
}

internal sealed class ModelVerifier(ISettingsStore settings, UserNotices? notices = null) : IModelVerifier
{
    public async Task<ModelVerificationResult> VerifyInstalledModelsAsync(CancellationToken cancellationToken = default)
    {
        var current = settings.Snapshot();

        var transcriptionRemoved = false;
        var transcriptionSettings = new AppSettings
        {
            ModelSize = AppSettings.DefaultModelSize,
            ModelsFolder = current.ModelsFolder
        };
        if (WhisperTranscriptionService.TryGetPinnedModel(transcriptionSettings.ModelSize, out var whisperSha256, out var whisperName))
        {
            transcriptionRemoved = await VerifyAsync(transcriptionSettings.GetModelFilePath(), whisperSha256, whisperName,
                cancellationToken).ConfigureAwait(false);
        }

        var summaryRemoved = await VerifyAsync(current.GetSummaryModelFilePath(), SummarizationService.ModelSha256,
            SummarizationService.ModelName, cancellationToken).ConfigureAwait(false);

        return new ModelVerificationResult(transcriptionRemoved, summaryRemoved);
    }

    private async Task<bool> VerifyAsync(string path, string sha256, string modelName, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return !await ModelIntegrity.EnsureVerifiedAsync(path, sha256, modelName, null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn($"ModelVerifier: could not verify {modelName}", ex);
            notices?.AddOnce(path, UserMessage.FromKey("NoticeModelVerificationFailed", modelName, ex.ToUserMessage()));
            return false;
        }
    }
}
