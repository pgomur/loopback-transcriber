namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class LiveWorkMailboxTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Items_AreTakenInTheOrderTheyWerePosted()
    {
        var mailbox = new LiveWorkMailbox<int, string>(capacity: 4);

        mailbox.Post(1);
        mailbox.Post(2);

        Assert.Equal(2, mailbox.Count);
        Assert.True(mailbox.TryPeek(out var peeked));
        Assert.Equal(1, peeked);
        Assert.True(mailbox.TryTake(out var first));
        Assert.True(mailbox.TryTake(out var second));
        Assert.False(mailbox.TryTake(out _));
        Assert.Equal((1, 2), (first, second));
    }

    [Fact]
    public void WhenFull_TheOldestItemIsDroppedAndCounted()
    {
        var dropped = new List<int>();
        var mailbox = new LiveWorkMailbox<int, string>(capacity: 2, dropped.Add);

        mailbox.Post(1);
        mailbox.Post(2);
        mailbox.Post(3);

        Assert.Equal(1, mailbox.Dropped);
        Assert.Equal(new[] { 1 }, dropped);
        Assert.True(mailbox.TryTake(out var oldestKept));
        Assert.Equal(2, oldestKept);
    }

    [Fact]
    public async Task WaitAsync_CompletesWhenSomethingIsPosted()
    {
        var mailbox = new LiveWorkMailbox<int, string>(capacity: 4);
        var waiting = mailbox.WaitAsync(CancellationToken.None);

        Assert.False(waiting.IsCompleted);

        mailbox.Post(1);

        await waiting.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ASignalSentBeforeWaiting_IsNotLost()
    {
        var mailbox = new LiveWorkMailbox<int, string>(capacity: 4);

        mailbox.Signal();

        await mailbox.WaitAsync(CancellationToken.None).WaitAsync(Timeout);
    }

    [Fact]
    public async Task SeveralSignals_WakeTheReaderOnlyOnce()
    {
        var mailbox = new LiveWorkMailbox<int, string>(capacity: 4);

        mailbox.Post(1);
        mailbox.Post(2);
        mailbox.Signal();

        await mailbox.WaitAsync(CancellationToken.None).WaitAsync(Timeout);
        var second = mailbox.WaitAsync(CancellationToken.None);

        Assert.False(second.IsCompleted);
        Assert.Equal(2, mailbox.Count);
    }

    [Fact]
    public async Task WaitAsync_StopsWhenCancelled()
    {
        var mailbox = new LiveWorkMailbox<int, string>(capacity: 4);
        using var cancellation = new CancellationTokenSource();

        var waiting = mailbox.WaitAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task SettingADraft_WakesTheReader_AndOnlyTheLatestDraftIsKept()
    {
        var mailbox = new LiveWorkMailbox<int, string>(capacity: 4);
        var waiting = mailbox.WaitAsync(CancellationToken.None);

        mailbox.SetDraft("primero");
        mailbox.SetDraft("segundo");
        await waiting.WaitAsync(Timeout);

        Assert.Equal("segundo", mailbox.PeekDraft());
        Assert.Equal("segundo", mailbox.TakeDraft());
        Assert.Null(mailbox.TakeDraft());
    }

    [Fact]
    public void ClearDraft_DiscardsThePendingDraft()
    {
        var mailbox = new LiveWorkMailbox<int, string>(capacity: 4);
        mailbox.SetDraft("borrador");

        mailbox.ClearDraft();

        Assert.Null(mailbox.PeekDraft());
    }

    [Fact]
    public async Task Complete_LetsTheReaderDrainWhatIsLeft_ThenReportsCompletion()
    {
        var mailbox = new LiveWorkMailbox<int, string>(capacity: 4);
        mailbox.Post(1);

        mailbox.Complete();
        mailbox.Post(2);

        await mailbox.WaitAsync(CancellationToken.None).WaitAsync(Timeout);
        Assert.False(mailbox.IsCompleted);
        Assert.True(mailbox.TryTake(out var last));
        Assert.Equal(1, last);
        Assert.False(mailbox.TryTake(out _));
        Assert.True(mailbox.IsCompleted);
    }
}
