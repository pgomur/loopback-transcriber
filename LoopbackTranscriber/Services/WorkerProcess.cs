using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace LoopbackTranscriber.Services;

internal static class WorkerProcess
{
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

            var startInfo = CreateStartInfo(kind, jobPath, resultPath);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, _) => exited.TrySetResult();

            process.OutputDataReceived += (_, e) =>
            {
                if (WorkerProtocol.TryParseProgress(e.Data, out var value))
                {
                    onProgress?.Invoke(value);
                }
            };

            var stderrTail = new WorkerStderrTail();
            process.ErrorDataReceived += (_, e) => stderrTail.Add(e.Data);

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
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    AppLog.Warn("WorkerProcess: could not cancel the worker", ex);
                }
            });

            await exited.Task.ConfigureAwait(false);
            await DrainOutputAsync(process, kind).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(resultPath))
            {
                return await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false);
            }

            AppLog.Error($"WorkerProcess[{kind}]: the worker exited without a result (code {process.ExitCode}).\n{stderrTail.Dump()}");

            throw UserError.InvalidOperation("ErrorBackgroundProcessEnded", process.ExitCode);
        }
        finally
        {
            TryDelete(jobPath);
            TryDelete(resultPath);
        }
    }

    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(5);

    private static async Task DrainOutputAsync(Process process, string kind)
    {
        using var timeout = new CancellationTokenSource(OutputDrainTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AppLog.Info($"WorkerProcess[{kind}]: the worker's output did not close within {OutputDrainTimeout.TotalSeconds:0}s; continuing without it");
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string kind, string jobPath, string resultPath, bool redirectInput = false)
    {
        var executable = Environment.ProcessPath
                         ?? throw UserError.InvalidOperation("ErrorBackgroundProcessStart");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (redirectInput)
        {
            startInfo.RedirectStandardInput = true;
            startInfo.StandardInputEncoding = new System.Text.UTF8Encoding(false);
        }

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

        return startInfo;
    }

    private static void TryDelete(string path) => FileCleanup.TryDelete(path, "WorkerProcess");
}

internal static class WorkerHost
{
    public const string Flag = "--worker";

    public static bool IsWorkerInvocation(string[] commandLineArgs) =>
        commandLineArgs.Length >= 5 && string.Equals(commandLineArgs[1], Flag, StringComparison.Ordinal);

    public static int Run(string[] commandLineArgs)
    {
        var kind = commandLineArgs[2];
        InstallUnhandledExceptionLogging(kind);
        var jobPath = commandLineArgs[3];
        var resultPath = commandLineArgs[4];

        if (commandLineArgs.Length > 5 && int.TryParse(commandLineArgs[5], out var parentPid))
        {
            StartParentWatchdog(parentPid);
        }

        if (string.Equals(kind, "whisper-live", StringComparison.Ordinal))
        {
            return RunLiveWhisper(jobPath);
        }

        if (string.Equals(kind, "translate-live", StringComparison.Ordinal))
        {
            return RunLiveTranslator(jobPath);
        }

        try
        {
            var jobJson = File.ReadAllText(jobPath);

            var resultJson = Task.Run(() => kind switch
            {
                "whisper" => RunWhisperAsync(jobJson),
                "summary" => RunSummaryAsync(jobJson),
                _ => throw new InvalidOperationException($"Unknown job type: {kind}")
            }).GetAwaiter().GetResult();

            File.WriteAllText(resultPath, resultJson);
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"WorkerHost: job '{kind}' failed", ex);

            try
            {
                var error = new WorkerErrorResult { Error = ex.Message };
                if (UserError.TryGetKey(ex, out var errorKey, out var errorArgs))
                {
                    error.ErrorKey = errorKey;
                    error.ErrorArgs = UserError.ToTextArgs(errorArgs);
                }

                File.WriteAllText(resultPath, JsonSerializer.Serialize(error));
            }
            catch (Exception writeEx) when (ExpectedFailures.IsFile(writeEx))
            {
                AppLog.Warn("WorkerHost: could not write the job error", writeEx);
            }

            return 1;
        }
    }

    private static int RunLiveWhisper(string configPath)
    {
        try
        {
            var config = JsonSerializer.Deserialize<LiveWhisperConfig>(File.ReadAllText(configPath))
                         ?? throw new InvalidOperationException("Invalid live transcription configuration.");

            using var engine = new LiveWhisperEngine(config);
            WriteLiveMessage(new LiveWorkerMessage { Ready = true });

            string? line;
            while ((line = Console.In.ReadLine()) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var request = JsonSerializer.Deserialize<LiveWhisperRequest>(line);
                if (request is null)
                {
                    continue;
                }

                var response = new LiveWorkerMessage { Id = request.Id };
                try
                {
                    var pcm = Convert.FromBase64String(request.Audio);
                    var stopwatch = Stopwatch.StartNew();
                    response.Segments = Task.Run(() => engine.TranscribeAsync(pcm)).GetAwaiter().GetResult();
                    response.Ms = stopwatch.ElapsedMilliseconds;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("WorkerHost: a live request failed", ex);
                    response.Error = ex.Message;
                }

                WriteLiveMessage(response);
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteLiveMessage(new LiveWorkerMessage { Error = ex.Message });
            return 1;
        }
    }

    private static int RunLiveTranslator(string configPath)
    {
        try
        {
            var config = JsonSerializer.Deserialize<LiveTranslatorConfig>(File.ReadAllText(configPath))
                         ?? throw new InvalidOperationException("Invalid live translation configuration.");

            using var engine = new LiveTranslatorEngine(config);

            Task.Run(() => engine.TranslateAsync("Hello, how are you?", null, "Spanish")).GetAwaiter().GetResult();

            WriteLiveMessage(new LiveWorkerMessage { Ready = true });

            string? line;
            while ((line = Console.In.ReadLine()) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var request = JsonSerializer.Deserialize<LiveTranslateRequest>(line);
                if (request is null)
                {
                    continue;
                }

                var response = new LiveWorkerMessage { Id = request.Id };
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    response.Text = Task.Run(() => engine.TranslateAsync(request.Text, request.Context, request.Target))
                        .GetAwaiter().GetResult();
                    response.Ms = stopwatch.ElapsedMilliseconds;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("WorkerHost: a live request failed", ex);
                    response.Error = ex.Message;
                }

                WriteLiveMessage(response);
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteLiveMessage(new LiveWorkerMessage { Error = ex.Message });
            return 1;
        }
    }

    private static void WriteLiveMessage(LiveWorkerMessage message)
    {
        try
        {
            Console.Out.WriteLine(WorkerProtocol.FormatLiveMessage(message));
            Console.Out.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            AppLog.Warn("WorkerHost: could not send a live transcription message", ex);
        }
    }

    private static void InstallUnhandledExceptionLogging(string kind)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error($"WorkerHost[{kind}]: unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Warn($"WorkerHost[{kind}]: unobserved task exception", e.Exception);
            e.SetObserved();
        };
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
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                AppLog.Info($"WorkerHost: the parent process {parentPid} is already gone ({ex.GetType().Name}); exiting");
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
            Console.Out.WriteLine(WorkerProtocol.FormatProgress(value));
            Console.Out.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            AppLog.Warn("WorkerHost: could not report progress", ex);
        }
    }

    private sealed class ConsoleProgress<T>(Func<T, double> toValue) : IProgress<T>
    {
        public void Report(T value) => ReportProgress(toValue(value));
    }

    private static async Task<string> RunWhisperAsync(string jobJson)
    {
        var job = JsonSerializer.Deserialize<WhisperJob>(jobJson)
                  ?? throw new InvalidOperationException("Invalid transcription job.");

        var transcription = await WhisperTranscriptionService.TranscribeInProcessAsync(job, new ConsoleProgress<int>(p => p));

        var result = new WhisperJobResult
        {
            Language = transcription.Language,
            Segments = transcription.Segments
                .Select(s => new SegmentDto { StartTicks = s.Start.Ticks, EndTicks = s.End.Ticks, Text = s.Text })
                .ToList()
        };

        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> RunSummaryAsync(string jobJson)
    {
        var job = JsonSerializer.Deserialize<SummaryJob>(jobJson)
                  ?? throw new InvalidOperationException("Invalid summary job.");

        using var service = new SummarizationService();
        var summary = await service.SummarizeInProcessAsync(job.TranscriptText, job.ModelPath, job.UiLanguage,
            new ConsoleProgress<double>(p => p));

        return JsonSerializer.Serialize(new SummaryJobResult { Title = summary.Title, Summary = summary.Summary });
    }
}

internal sealed class WorkerErrorResult
{
    public string? Error { get; set; }
    public string? ErrorKey { get; set; }
    public string[]? ErrorArgs { get; set; }
}

internal sealed class WhisperJob
{
    public string WavPath { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? FallbackLanguage { get; set; }
    public int ThreadCount { get; set; }
    public bool StrictNoSpeech { get; set; }
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
    public string? ErrorKey { get; set; }
    public string[]? ErrorArgs { get; set; }
    public string? Language { get; set; }
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
    public string? ErrorKey { get; set; }
    public string[]? ErrorArgs { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
}
