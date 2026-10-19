using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace LoopbackTranscriber.Services;

internal sealed class LiveWorkMailbox<TItem, TDraft> where TDraft : class
{
    private readonly Channel<TItem> _items;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private TDraft? _draft;
    private int _dropped;

    public LiveWorkMailbox(int capacity, Action<TItem>? onDropped = null)
    {
        _items = Channel.CreateBounded<TItem>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            },
            item =>
            {
                Interlocked.Increment(ref _dropped);
                onDropped?.Invoke(item);
            });
    }

    public int Count => _items.Reader.Count;

    public int Dropped => Volatile.Read(ref _dropped);

    public bool IsCompleted => _items.Reader.Completion.IsCompleted;

    public void Post(TItem item)
    {
        _items.Writer.TryWrite(item);
        Signal();
    }

    public bool TryPeek([MaybeNullWhen(false)] out TItem item) => _items.Reader.TryPeek(out item);

    public bool TryTake([MaybeNullWhen(false)] out TItem item) => _items.Reader.TryRead(out item);

    public TDraft? PeekDraft() => Volatile.Read(ref _draft);

    public void SetDraft(TDraft draft)
    {
        Interlocked.Exchange(ref _draft, draft);
        Signal();
    }

    public TDraft? TakeDraft() => Interlocked.Exchange(ref _draft, null);

    public void ClearDraft() => Interlocked.Exchange(ref _draft, null);

    public Task WaitAsync(CancellationToken cancellationToken) => _signal.WaitAsync(cancellationToken);

    public void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public void Complete()
    {
        _items.Writer.TryComplete();
        Signal();
    }
}
