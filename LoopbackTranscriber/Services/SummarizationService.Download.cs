using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Sampling;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed partial class SummarizationService
{
    private const string ModelRevision = "bc640142c66e1fdd12af0bd68f40445458f3869b";

    internal const string ModelSha256 = "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5";

    internal const string ModelName = "Qwen3-4B-Q4_K_M";

    private const string ModelDownloadUrl =
        "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/" + ModelRevision + "/Qwen3-4B-Q4_K_M.gguf";

    private const int DownloadBufferSize = 1024 * 1024;

    public async Task EnsureModelDownloadedAsync(string destinationPath,
        IProgress<UserMessage>? progress = null, IProgress<double>? percentProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath)
            && await ModelIntegrity.EnsureVerifiedAsync(destinationPath, ModelSha256, ModelName, progress, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = destinationPath + ".download";

        try
        {
            var resumeFrom = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, ModelDownloadUrl);
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

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
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
                    progress?.Report(UserMessage.FromKey("SummaryModelDownloadingWithPercent",
                        $"{percent:0}", FormatBytes(totalRead), FormatBytes(totalBytes.Value)));
                }
                else
                {
                    progress?.Report(UserMessage.FromKey("SummaryModelDownloadingBytes", FormatBytes(totalRead)));
                }
            }

            if (totalBytes is > 0 && totalRead != totalBytes.Value)
            {
                throw UserError.IO("ErrorDownloadIncomplete", totalRead, totalBytes.Value);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        try
        {
            await ModelIntegrity.VerifySha256Async(tempPath, ModelSha256, ModelName, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        ModelIntegrity.WriteMarker(destinationPath, ModelSha256);
        percentProgress?.Report(100);
        progress?.Report(UserMessage.FromKey("SummaryModelDownloaded"));
    }

    internal static string FormatBytes(long bytes)
    {
        const double mb = 1024d * 1024d;
        var megabytes = bytes / mb;
        return megabytes >= 1024
            ? $"{megabytes / 1024:0.0} GB"
            : $"{megabytes:0} MB";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn($"SummarizationService: could not delete temp file \"{path}\"", ex);
        }
    }

    public static void DiscardPartialDownload(string destinationPath)
    {
        TryDeleteFile(destinationPath + ".download");
    }
}
