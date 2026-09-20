using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LoopbackTranscriber.Services;

public sealed class WhisperTranscriptionService
{

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromHours(1) };

    private const string HuggingFaceRepository = "https://huggingface.co/sandrohanea/whisper.net/resolve";
    private const string ModelVersion = "v5";

    public static GgmlType ParseModelSize(string modelSize) => modelSize.Trim().ToLowerInvariant() switch
    {
        "tiny" => GgmlType.Tiny,
        "base" => GgmlType.Base,
        "small" => GgmlType.Small,
        "medium" => GgmlType.Medium,
        "large" => GgmlType.LargeV3,
        _ => GgmlType.Small
    };

    private static string GetModelFileName(GgmlType type) => type switch
    {
        GgmlType.Tiny => "ggml-tiny",
        GgmlType.Base => "ggml-base",
        GgmlType.Small => "ggml-small",
        GgmlType.Medium => "ggml-medium",
        GgmlType.LargeV3 => "ggml-large-v3",
        _ => "ggml-small"
    };

    private static string GetModelUrl(GgmlType type) =>
        $"{HuggingFaceRepository}/{ModelVersion}/classic/{GetModelFileName(type)}.bin";

    internal static string FormatBytes(long bytes)
    {
        const double mb = 1024d * 1024d;
        var megabytes = bytes / mb;
        return megabytes >= 1024
            ? $"{megabytes / 1024:0.0} GB"
            : $"{megabytes:0} MB";
    }

    public async Task EnsureModelDownloadedAsync(string modelSize, string destinationPath,
        IProgress<string>? progress = null, IProgress<double>? percentProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var ggmlType = ParseModelSize(modelSize);
        var tempPath = destinationPath + ".download";

        var downloadedWithProgress = await TryDownloadWithProgressAsync(ggmlType, modelSize, tempPath, progress,
            percentProgress, cancellationToken).ConfigureAwait(false);

        if (!downloadedWithProgress)
        {
            progress?.Report($"Descargando modelo Whisper ({modelSize})...");

            await using (var modelStream = await WhisperGgmlDownloader.Default
                             .GetGgmlModelAsync(ggmlType, cancellationToken: cancellationToken)
                             .ConfigureAwait(false))
            await using (var fileStream = File.Create(tempPath))
            {
                await modelStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        percentProgress?.Report(100);
        progress?.Report("Modelo descargado.");
    }

    private static async Task<bool> TryDownloadWithProgressAsync(GgmlType type, string modelSize, string tempPath,
        IProgress<string>? progress, IProgress<double>? percentProgress, CancellationToken cancellationToken)
    {
        try
        {
            var url = GetModelUrl(type);

            using var response = await HttpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var fileStream = File.Create(tempPath);

            var buffer = new byte[81920];
            long totalRead = 0;
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
                    progress?.Report(
                        $"Descargando modelo ({modelSize}): {percent:0}% ({FormatBytes(totalRead)} / {FormatBytes(totalBytes.Value)})");
                }
                else
                {
                    progress?.Report($"Descargando modelo ({modelSize}): {FormatBytes(totalRead)} descargados...");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"WhisperTranscriptionService: fallo la descarga con progreso del modelo \"{modelSize}\", se usara el descargador de respaldo", ex);

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanupEx)
            {
                AppLog.Warn($"WhisperTranscriptionService: no se pudo borrar el fichero temporal \"{tempPath}\"", cleanupEx);
            }

            return false;
        }
    }

    public async Task<string> TranscribeAsync(string wavPath, string modelPath, string language,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        using var whisperFactory = WhisperFactory.FromPath(modelPath);
        using var processor = whisperFactory.CreateBuilder()
            .WithLanguage(language)
            .Build();

        await using var fileStream = File.OpenRead(wavPath);

        var builder = new StringBuilder();
        await foreach (var result in processor.ProcessAsync(fileStream, cancellationToken).ConfigureAwait(false))
        {
            var text = result.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(text);
            progress?.Report(builder.ToString());
        }

        return builder.ToString();
    }
}
