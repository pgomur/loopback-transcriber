using System.Collections.Concurrent;

namespace LoopbackTranscriber.Services;

public sealed class UserNotices
{
    private readonly ConcurrentQueue<UserMessage> _pending = new();
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Added;

    public void Add(UserMessage message)
    {
        _pending.Enqueue(message);
        Added?.Invoke();
    }

    public void AddOnce(string identity, UserMessage message)
    {
        if (_reported.TryAdd(identity, 0))
        {
            Add(message);
        }
    }

    public IReadOnlyList<UserMessage> TakeAll()
    {
        var taken = new List<UserMessage>();
        while (_pending.TryDequeue(out var message))
        {
            taken.Add(message);
        }

        return taken;
    }
}
