namespace LoopbackTranscriber.Services;

internal sealed class LiveWorkerSupervisor : IDisposable
{
    private const int MaxRestarts = 3;

    private readonly Func<CancellationToken, Task<LiveWorkerClient>> _start;
    private readonly Func<bool> _isSessionDisposed;
    private readonly string _processName;
    private readonly string _requestErrorLog;
    private readonly string _requestErrorMetric;
    private LiveWorkerClient? _client;
    private int _restarts;
    private int _disposed;

    public LiveWorkerSupervisor(Func<CancellationToken, Task<LiveWorkerClient>> start, Func<bool> isSessionDisposed,
        string processName, string requestErrorLog, string requestErrorMetric)
    {
        _start = start;
        _isSessionDisposed = isSessionDisposed;
        _processName = processName;
        _requestErrorLog = requestErrorLog;
        _requestErrorMetric = requestErrorMetric;
    }

    public async Task StartAsync(CancellationToken token)
    {
        var client = await _start(token).ConfigureAwait(false);
        Interlocked.Exchange(ref _client, client);

        if (IsDisposed)
        {
            Interlocked.Exchange(ref _client, null)?.Dispose();
            throw new OperationCanceledException(token);
        }
    }

    public async Task<LiveWorkerMessage?> SendAsync(
        Func<LiveWorkerClient, CancellationToken, Task<LiveWorkerMessage>> request, CancellationToken token)
    {
        try
        {
            var client = Volatile.Read(ref _client) ?? throw new OperationCanceledException(token);
            return await request(client, token).ConfigureAwait(false);
        }
        catch (LiveWorkerRequestException ex) when (!token.IsCancellationRequested)
        {
            AppLog.Warn(_requestErrorLog, ex);
            LiveMetricsLog.Write(_requestErrorMetric + ex.Message);
            return null;
        }
        catch (Exception ex) when (IsRecoverable(ex, token) && _restarts < MaxRestarts)
        {
            _restarts++;
            AppLog.Warn($"LiveTranscriptionSession: restarting the {_processName} worker ({_restarts}/{MaxRestarts})", ex);
            LiveMetricsLog.Write($"{_processName} restart ({_restarts}/{MaxRestarts}): " + ex.Message);

            Interlocked.Exchange(ref _client, null)?.Dispose();
            await StartAsync(token).ConfigureAwait(false);
            return null;
        }
    }

    private bool IsRecoverable(Exception ex, CancellationToken token) =>
        !token.IsCancellationRequested
        && !IsDisposed
        && ex is TimeoutException or InvalidOperationException;

    private bool IsDisposed => _isSessionDisposed() || Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Interlocked.Exchange(ref _client, null)?.Dispose();
    }
}
