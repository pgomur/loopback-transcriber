using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace LoopbackTranscriber.Services;

internal static class WorkerProcess
{
    private const string ProgressPrefix = "@@P ";

    public static async Task<string> RunAsync(string kind, string jobJson, Action<double>? onProgress,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LoopbackTranscriber", "worker");
        Directory.CreateDirectory(folder);

        var id = Guid.NewGuid().ToString("N");
        var jobPath = Path.Combine(folder, id + ".job.json");
        var resultPath = Path.Combine(folder, id + ".result.json");

        try
        {
            await File.WriteAllTextAsync(jobPath, jobJson, cancellationToken).ConfigureAwait(false);

            var executable = Environment.ProcessPath
                             ?? throw new InvalidOperationException(Strings.Instance.Get("ErrorBackgroundProcessStart"));

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var assemblyPath = Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    startInfo.ArgumentList.Add(assemblyPath);
                }
            }

            startInfo.ArgumentList.Add(WorkerHost.Flag);
            startInfo.ArgumentList.Add(kind);
            startInfo.ArgumentList.Add(jobPath);
            startInfo.ArgumentList.Add(resultPath);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, _) => exited.TrySetResult();

            process.OutputDataReceived += (_, e) =>
            {
                var line = e.Data;
                if (line is null || !line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
                {
                    return;
                }

                if (double.TryParse(line.AsSpan(ProgressPrefix.Length), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var value))
                {
                    onProgress?.Invoke(value);
                }
            };

            process.ErrorDataReceived += (_, _) => { };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cancelRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn("WorkerProcess: no se pudo cancelar el proceso auxiliar", ex);
                }
            });

            await exited.Task.ConfigureAwait(false);
            process.WaitForExit();

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(resultPath))
            {
                return await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                Strings.Instance.Format("ErrorBackgroundProcessEnded", process.ExitCode));
        }
        finally
        {
            TryDelete(jobPath);
            TryDelete(resultPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"WorkerProcess: no se pudo borrar \"{path}\"", ex);
        }
    }
}

internal static class WorkerHost
{
    public const string Flag = "--worker";

    public static bool IsWorkerInvocation(string[] commandLineArgs) =>
        commandLineArgs.Length >= 5 && string.Equals(commandLineArgs[1], Flag, StringComparison.Ordinal);

    public static int Run(string[] commandLineArgs)
    {
        var kind = commandLineArgs[2];
        var jobPath = commandLineArgs[3];
        var resultPath = commandLineArgs[4];

        if (commandLineArgs.Length > 5 && int.TryParse(commandLineArgs[5], out var parentPid))
        {
            StartParentWatchdog(parentPid);
        }

        try
        {
            var jobJson = File.ReadAllText(jobPath);

            var resultJson = Task.Run(() => kind switch
            {
                "whisper" => RunWhisperAsync(jobJson),
                "summary" => RunSummaryAsync(jobJson),
                _ => throw new InvalidOperationException($"Tipo de trabajo desconocido: {kind}")
            }).GetAwaiter().GetResult();

            File.WriteAllText(resultPath, resultJson);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(resultPath, JsonSerializer.Serialize(new WorkerErrorResult { Error = ex.Message }));
            }
            catch (Exception writeEx)
            {
                AppLog.Warn("WorkerHost: no se pudo escribir el error del trabajo", writeEx);
            }

            return 1;
        }
    }

    private static void StartParentWatchdog(int parentPid)
    {
        var thread = new Thread(() =>
        {
            try
            {
                using var parent = Process.GetProcessById(parentPid);
                parent.WaitForExit();
            }
            catch
            {
            }

            Environment.Exit(2);
        })
        {
            IsBackground = true
        };

        thread.Start();
    }

    private static void ReportProgress(double value)
    {
        try
        {
            Console.Out.WriteLine("@@P " + value.ToString("0.###", CultureInfo.InvariantCulture));
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            AppLog.Warn("WorkerHost: no se pudo notificar el progreso", ex);
        }
    }

    private sealed class ConsoleProgress<T>(Func<T, double> toValue) : IProgress<T>
    {
        public void Report(T value) => ReportProgress(toValue(value));
    }

    private static async Task<string> RunWhisperAsync(string jobJson)
    {
        var job = JsonSerializer.Deserialize<WhisperJob>(jobJson)
                  ?? throw new InvalidOperationException("Trabajo de transcripcion invalido.");

        var service = new WhisperTranscriptionService();
        var segments = await service.TranscribeSegmentsInProcessAsync(job.WavPath, job.ModelPath, job.Language,
            job.ThreadCount, job.UseGpu, new ConsoleProgress<int>(p => p));

        var result = new WhisperJobResult
        {
            Segments = segments
                .Select(s => new SegmentDto { StartTicks = s.Start.Ticks, EndTicks = s.End.Ticks, Text = s.Text })
                .ToList()
        };

        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> RunSummaryAsync(string jobJson)
    {
        var job = JsonSerializer.Deserialize<SummaryJob>(jobJson)
                  ?? throw new InvalidOperationException("Trabajo de resumen invalido.");

        Strings.Instance.SetLanguage(job.UiLanguage);

        using var service = new SummarizationService();
        var summary = await service.SummarizeInProcessAsync(job.TranscriptText, job.ModelPath,
            new ConsoleProgress<double>(p => p));

        return JsonSerializer.Serialize(new SummaryJobResult { Title = summary.Title, Summary = summary.Summary });
    }
}

internal sealed class WorkerErrorResult
{
    public string? Error { get; set; }
}

internal sealed class WhisperJob
{
    public string WavPath { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int ThreadCount { get; set; }
    public bool UseGpu { get; set; }
}

internal sealed class SegmentDto
{
    public long StartTicks { get; set; }
    public long EndTicks { get; set; }
    public string Text { get; set; } = string.Empty;
}

internal sealed class WhisperJobResult
{
    public string? Error { get; set; }
    public List<SegmentDto> Segments { get; set; } = new();
}

internal sealed class SummaryJob
{
    public string TranscriptText { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string UiLanguage { get; set; } = "es";
}

internal sealed class SummaryJobResult
{
    public string? Error { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
}
