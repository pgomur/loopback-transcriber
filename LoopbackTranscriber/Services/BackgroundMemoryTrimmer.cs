namespace LoopbackTranscriber.Services;

internal sealed class BackgroundMemoryTrimmer : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private Timer? _timer;

    public void Enter()
    {
        if (_timer is null)
        {
            _timer = new Timer(_ => MemoryTrimmer.TrimWorkingSet(), null, Interval, Interval);
        }
        else
        {
            _timer.Change(Interval, Interval);
        }
    }

    public void Leave() => _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
