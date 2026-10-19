using System.Diagnostics;
using System.Linq;
using NAudio.Wave;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Whisper.net;
using Whisper.net.Ggml;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LoopbackTranscriber.Services;

public sealed partial class WhisperTranscriptionService
{

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromHours(1) };

    private const string HuggingFaceRepository = "https://huggingface.co/sandrohanea/whisper.net/resolve";

    internal const string ModelRevision = "79dea24237e62d8a6e20a978ba415446d19712b3";

    internal static readonly IReadOnlyDictionary<GgmlType, string> PinnedSha256 = new Dictionary<GgmlType, string>
    {
        [GgmlType.LargeV3Turbo] = "05265418cae607e28c62db4095612e2c1967885d87cbda5a104834414b563ac8",
    };

    private const int DownloadBufferSize = 1024 * 1024;

    public static GgmlType ParseModelSize(string modelSize) => modelSize.Trim().ToLowerInvariant() switch
    {
        "tiny" => GgmlType.Tiny,
        "base" => GgmlType.Base,
        "small" => GgmlType.Small,
        "medium" => GgmlType.Medium,
        "large" => GgmlType.LargeV3,
        "turbo" => GgmlType.LargeV3Turbo,
        _ => throw UserError.InvalidOperation("ErrorModelNotPinned", modelSize)
    };

    private static string GetModelFileName(GgmlType type) => type switch
    {
        GgmlType.Tiny => "ggml-tiny",
        GgmlType.Base => "ggml-base",
        GgmlType.Small => "ggml-small",
        GgmlType.Medium => "ggml-medium",
        GgmlType.LargeV3 => "ggml-large-v3",
        GgmlType.LargeV3Turbo => "ggml-large-v3-turbo",
        _ => "ggml-small"
    };

    internal static bool TryGetPinnedModel(string modelSize, out string sha256, out string modelName)
    {
        var type = ParseModelSize(modelSize);
        modelName = GetModelFileName(type);

        if (PinnedSha256.TryGetValue(type, out var pinned))
        {
            sha256 = pinned;
            return true;
        }

        sha256 = string.Empty;
        return false;
    }

    private static string GetModelUrl(GgmlType type) =>
        $"{HuggingFaceRepository}/{ModelRevision}/q5_1/{GetModelFileName(type)}.bin";

    internal static string FormatBytes(long bytes)
    {
        const double mb = 1024d * 1024d;
        var megabytes = bytes / mb;
        return megabytes >= 1024
            ? $"{megabytes / 1024:0.0} GB"
            : $"{megabytes:0} MB";
    }

    public async Task EnsureModelDownloadedAsync(string modelSize, string destinationPath,
        IProgress<UserMessage>? progress = null, IProgress<double>? percentProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath))
        {
            if (!TryGetPinnedModel(modelSize, out var existingSha256, out var existingName)
                || await ModelIntegrity.EnsureVerifiedAsync(destinationPath, existingSha256, existingName, progress,
                    cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var ggmlType = ParseModelSize(modelSize);

        if (!PinnedSha256.TryGetValue(ggmlType, out var expectedSha256))
        {
            throw UserError.InvalidOperation("ErrorModelNotPinned", GetModelFileName(ggmlType));
        }

        var tempPath = destinationPath + ".download";

        var downloaded = await TryDownloadWithProgressAsync(ggmlType, modelSize, tempPath, progress,
            percentProgress, cancellationToken).ConfigureAwait(false);

        if (!downloaded)
        {
            progress?.Report(UserMessage.FromKey("ProgressDownloadingModel"));
            downloaded = await TryDownloadWithProgressAsync(ggmlType, modelSize, tempPath, progress,
                percentProgress, cancellationToken).ConfigureAwait(false);
        }

        if (!downloaded)
        {
            throw UserError.IO("ErrorModelDownloadFailed", GetModelFileName(ggmlType));
        }

        try
        {
            await ModelIntegrity.VerifySha256Async(tempPath, expectedSha256, GetModelFileName(ggmlType), progress,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception deleteEx)
            {
                AppLog.Warn($"WhisperTranscriptionService: could not delete \"{tempPath}\"", deleteEx);
            }

            throw;
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        ModelIntegrity.WriteMarker(destinationPath, expectedSha256);
        percentProgress?.Report(100);
        progress?.Report(UserMessage.FromKey("ProgressModelDownloaded"));
    }

    private static async Task<bool> TryDownloadWithProgressAsync(GgmlType type, string modelSize, string tempPath,
        IProgress<UserMessage>? progress, IProgress<double>? percentProgress, CancellationToken cancellationToken)
    {
        try
        {
            var url = GetModelUrl(type);
            var resumeFrom = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (resumeFrom > 0)
            {
                request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
            }

            using var response = await HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var isResuming = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!isResuming)
            {
                resumeFrom = 0;
            }

            response.EnsureSuccessStatusCode();

            long? totalBytes = isResuming
                ? response.Content.Headers.ContentRange?.Length ?? (resumeFrom + response.Content.Headers.ContentLength)
                : response.Content.Headers.ContentLength;

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var fileStream = isResuming
                ? new FileStream(tempPath, FileMode.Append, FileAccess.Write, FileShare.None, DownloadBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan)
                : new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, DownloadBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[DownloadBufferSize];
            long totalRead = resumeFrom;
            var stopwatch = Stopwatch.StartNew();
            int read;

            while ((read = await httpStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalRead += read;

                if (stopwatch.ElapsedMilliseconds < 200)
                {
                    continue;
                }

                stopwatch.Restart();

                if (totalBytes is > 0)
                {
                    var percent = Math.Min(100.0, totalRead * 100.0 / totalBytes.Value);
                    percentProgress?.Report(percent);
                    progress?.Report(UserMessage.FromKey("ProgressDownloadingWithPercent",
                        $"{percent:0}", FormatBytes(totalRead), FormatBytes(totalBytes.Value)));
                }
                else
                {
                    progress?.Report(UserMessage.FromKey("ProgressDownloadingBytes", FormatBytes(totalRead)));
                }
            }

            if (totalBytes is > 0 && totalRead != totalBytes.Value)
            {
                throw UserError.IO("ErrorDownloadIncomplete", totalRead, totalBytes.Value);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"WhisperTranscriptionService: the progress download of model \"{modelSize}\" failed; using the fallback downloader", ex);

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanupEx)
            {
                AppLog.Warn($"WhisperTranscriptionService: could not delete temp file \"{tempPath}\"", cleanupEx);
            }

            return false;
        }
    }

    public static void DiscardPartialDownload(string destinationPath)
    {
        var tempPath = destinationPath + ".download";
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"WhisperTranscriptionService: could not delete temp file \"{tempPath}\"", ex);
        }
    }
}
