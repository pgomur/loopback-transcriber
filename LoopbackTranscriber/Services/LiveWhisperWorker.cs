using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Whisper.net;

namespace LoopbackTranscriber.Services;

internal sealed class LiveWhisperConfig
{
    public string ModelPath { get; set; } = string.Empty;
    public string Language { get; set; } = "auto";
    public int ThreadCount { get; set; } = 4;
    public bool UseGpu { get; set; } = true;

    public int AudioContext { get; set; }

    public bool FlashAttention { get; set; }
}

internal sealed class LiveWhisperRequest
{
    public int Id { get; set; }

    public string Audio { get; set; } = string.Empty;
}

internal sealed class LiveWorkerMessage
{
    public bool Ready { get; set; }
    public int Id { get; set; }
    public string? Error { get; set; }
    public long Ms { get; set; }
    public List<SegmentDto>? Segments { get; set; }

    public string? Text { get; set; }
}

internal sealed class LiveWhisperEngine : IDisposable
{
    private const int MinSamples = 16000;

    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly string? _filterLanguage;

    public LiveWhisperEngine(LiveWhisperConfig config)
    {
        WhisperFactory factory;
        try
        {
            factory = WhisperFactory.FromPath(config.ModelPath,
                new WhisperFactoryOptions { UseGpu = config.UseGpu, UseFlashAttention = config.FlashAttention });
        }
        catch (Exception) when (config.FlashAttention)
        {
            factory = WhisperFactory.FromPath(config.ModelPath, new WhisperFactoryOptions { UseGpu = config.UseGpu });
        }

        _factory = factory;
        _filterLanguage = string.Equals(config.Language, WhisperTranscriptionService.AutoLanguage, StringComparison.OrdinalIgnoreCase)
            ? null
            : config.Language;

        var builder = _factory.CreateBuilder()
            .WithLanguage(config.Language)
            .WithThreads(config.ThreadCount)
            .WithNoContext()
            .WithNoSpeechThreshold(0.6f)
            .WithProbabilities();

        if (config.AudioContext > 0)
        {
            builder = builder.WithAudioContextSize(config.AudioContext);
        }

        _processor = builder.Build();
    }

    public async Task<List<SegmentDto>> TranscribeAsync(byte[] pcm16)
    {
        var dataLength = Math.Max(pcm16.Length, MinSamples * 2);

        using var stream = new MemoryStream(44 + dataLength);
        WriteWavHeader(stream, dataLength);
        stream.Write(pcm16, 0, pcm16.Length);
        if (dataLength > pcm16.Length)
        {
            stream.Write(new byte[dataLength - pcm16.Length]);
        }

        stream.Position = 0;

        var segments = new List<SegmentDto>();
        await foreach (var result in _processor.ProcessAsync(stream).ConfigureAwait(false))
        {
            var text = WhisperTranscriptionService.FilterSegmentText(result.Text, result.NoSpeechProbability, _filterLanguage);
            if (text is null)
            {
                continue;
            }

            segments.Add(new SegmentDto
            {
                StartTicks = result.Start.Ticks,
                EndTicks = result.End.Ticks,
                Text = text
            });
        }

        return segments;
    }

    private static void WriteWavHeader(Stream stream, int dataLength)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16000);
        writer.Write(16000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
    }
}

internal sealed class LiveWorkerClient : IDisposable
{
    private const string MessagePrefix = "@@L ";

    private readonly Process _process;
    private readonly string _configPath;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _lock = new();
    private readonly Dictionary<int, TaskCompletionSource<LiveWorkerMessage>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly WorkerStderrTail _stderrTail = new();
    private readonly string _kind;
    private StreamWriter? _stdin;
    private int _nextId;
    private bool _disposed;

    private LiveWorkerClient(Process process, string configPath, string kind)
    {
        _process = process;
        _configPath = configPath;
        _kind = kind;
    }

    public static Task<LiveWorkerClient> StartWhisperAsync(LiveWhisperConfig config, CancellationToken cancellationToken) =>
        StartAsync("whisper-live", JsonSerializer.Serialize(config), cancellationToken);

    public static Task<LiveWorkerClient> StartTranslatorAsync(LiveTranslatorConfig config, CancellationToken cancellationToken) =>
        StartAsync("translate-live", JsonSerializer.Serialize(config), cancellationToken);

    private static async Task<LiveWorkerClient> StartAsync(string kind, string configJson, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LoopbackTranscriber", "worker");
        Directory.CreateDirectory(folder);

        var configPath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".live.json");
        await File.WriteAllTextAsync(configPath, configJson, cancellationToken).ConfigureAwait(false);

        var startInfo = WorkerProcess.CreateStartInfo(kind, configPath, configPath + ".unused", redirectInput: true);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var client = new LiveWorkerClient(process, configPath, kind);

        process.OutputDataReceived += client.OnOutputLine;
        process.ErrorDataReceived += (_, e) => client._stderrTail.Add(e.Data);
        process.Exited += client.OnProcessExited;

        try
        {
            process.Start();
            client._stdin = process.StandardInput;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cancelRegistration = cancellationToken.Register(client.Dispose);

            await client._ready.Task.WaitAsync(TimeSpan.FromSeconds(180), cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public Task<LiveWorkerMessage> TranscribeAsync(short[] samples, CancellationToken cancellationToken)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        var audio = Convert.ToBase64String(bytes);

        return SendAsync(id => JsonSerializer.Serialize(new LiveWhisperRequest { Id = id, Audio = audio }),
            TimeSpan.FromSeconds(60), cancellationToken);
    }

    public Task<LiveWorkerMessage> TranslateAsync(string text, string? context, string target, CancellationToken cancellationToken) =>
        SendAsync(id => JsonSerializer.Serialize(new LiveTranslateRequest { Id = id, Text = text, Context = context, Target = target }),
            TimeSpan.FromSeconds(30), cancellationToken);

    private async Task<LiveWorkerMessage> SendAsync(Func<int, string> buildRequestLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<LiveWorkerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Interlocked.Increment(ref _nextId);

        lock (_lock)
        {
            if (_disposed || _process.HasExited)
            {
                throw UserError.InvalidOperation("ErrorBackgroundProcessStart");
            }

            _pending[id] = completion;
        }

        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stdin!.WriteLineAsync(buildRequestLine(id)).ConfigureAwait(false);
                await _stdin.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }

            var response = await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(response.Error))
            {
                throw new LiveWorkerRequestException(response.Error);
            }

            return response;
        }
        finally
        {
            lock (_lock)
            {
                _pending.Remove(id);
            }
        }
    }

    private void OnOutputLine(object sender, DataReceivedEventArgs e)
    {
        var line = e.Data;
        if (line is null || !line.StartsWith(MessagePrefix, StringComparison.Ordinal))
        {
            return;
        }

        LiveWorkerMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<LiveWorkerMessage>(line.AsSpan(MessagePrefix.Length));
        }
        catch (JsonException ex)
        {
            AppLog.Warn("LiveWorkerClient: unreadable message from the worker", ex);
            return;
        }

        if (message is null)
        {
            return;
        }

        if (message.Ready)
        {
            _ready.TrySetResult(true);
            return;
        }

        if (message.Id == 0 && !string.IsNullOrEmpty(message.Error))
        {
            var error = new InvalidOperationException(message.Error);
            _ready.TrySetException(error);
            FailPending(error);
            return;
        }

        TaskCompletionSource<LiveWorkerMessage>? pending;
        lock (_lock)
        {
            if (!_pending.Remove(message.Id, out pending))
            {
                AppLog.Info($"LiveWorkerClient[{_kind}]: discarded response (Id {message.Id} is no longer pending)");
                return;
            }
        }

        pending.TrySetResult(message);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = SafeExitCode();
        if (!_disposed)
        {
            AppLog.Error($"LiveWorkerClient[{_kind}]: the worker exited unexpectedly (code {exitCode}).\n{_stderrTail.Dump()}");
        }

        var error = UserError.InvalidOperation("ErrorBackgroundProcessEnded", exitCode);
        _ready.TrySetException(error);
        FailPending(error);
    }

    private int SafeExitCode()
    {
        try
        {
            return _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private void FailPending(Exception error)
    {
        TaskCompletionSource<LiveWorkerMessage>[] pending;
        lock (_lock)
        {
            pending = _pending.Values.ToArray();
            _pending.Clear();
        }

        foreach (var completion in pending)
        {
            completion.TrySetException(error);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        var cancelled = new OperationCanceledException();
        _ready.TrySetException(cancelled);
        FailPending(cancelled);

        try
        {
            _stdin?.Close();
        }
        catch (Exception ex)
        {
            AppLog.Warn("LiveWorkerClient: could not close the worker's stdin", ex);
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("LiveWorkerClient: could not kill the worker", ex);
        }

        try
        {
            _process.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Warn("LiveWorkerClient: could not dispose the worker process", ex);
        }

        try
        {
            if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"LiveWorkerClient: could not delete \"{_configPath}\"", ex);
        }
    }
}

internal sealed class LiveWorkerRequestException(string message) : InvalidOperationException(message);
