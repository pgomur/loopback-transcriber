namespace LoopbackTranscriber.Tests.Infrastructure;

public sealed class CollectingProgress<T> : IProgress<T>
{
    private readonly object _gate = new();
    private readonly List<T> _reports = [];

    public IReadOnlyList<T> Reports
    {
        get
        {
            lock (_gate)
            {
                return _reports.ToList();
            }
        }
    }

    public void Report(T value)
    {
        lock (_gate)
        {
            _reports.Add(value);
        }
    }
}
