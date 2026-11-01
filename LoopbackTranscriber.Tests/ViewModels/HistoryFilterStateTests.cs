namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Unit)]
public sealed class HistoryFilterStateTests
{
    private static TranscriptHistoryItem Item(string name, DateTime timestamp, TimeSpan? duration) => new()
    {
        AppName = "Zoom",
        Timestamp = timestamp,
        Preview = name,
        FilePath = name,
        HasTxt = true,
        HasMarkdown = false,
        HasAudio = false,
        HasSummary = false,
        SummaryTitle = null,
        SummaryText = null,
        SummaryFilePath = name + ".summary.json",
        Duration = duration
    };

    private static List<TranscriptHistoryItem> Sample() =>
    [
        Item("enero", new DateTime(2026, 1, 10, 9, 0, 0), TimeSpan.FromMinutes(5)),
        Item("marzo", new DateTime(2026, 3, 10, 9, 0, 0), TimeSpan.FromMinutes(45)),
        Item("sin-duracion", new DateTime(2026, 3, 11, 9, 0, 0), null)
    ];

    [Fact]
    public void WithoutCriteria_EverythingMatches_AndNothingIsActive()
    {
        var filter = new HistoryFilterState(() => { });

        Assert.False(filter.HasActiveFilter);
        Assert.Equal(3, filter.Filter(Sample()).Count());
    }

    [Fact]
    public void DateRange_IsInclusiveOnBothEnds()
    {
        var filter = new HistoryFilterState(() => { })
        {
            FromDate = new DateTime(2026, 3, 10),
            ToDate = new DateTime(2026, 3, 10)
        };

        Assert.Equal(new[] { "marzo" }, filter.Filter(Sample()).Select(i => i.FilePath));
    }

    [Fact]
    public void MinDuration_ExcludesShortAndUnknownDurations()
    {
        var filter = new HistoryFilterState(() => { }) { MinDurationMinutes = 30 };

        Assert.Equal(new[] { "marzo" }, filter.Filter(Sample()).Select(i => i.FilePath));
    }

    [Fact]
    public void ChangingACriterion_NotifiesOnce_AndUpdatesHasActiveFilter()
    {
        var changes = 0;
        var filter = new HistoryFilterState(() => changes++);
        var notified = new List<string?>();
        filter.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        filter.FromDate = new DateTime(2026, 2, 1);

        Assert.Equal(1, changes);
        Assert.Contains(nameof(HistoryFilterState.HasActiveFilter), notified);
        Assert.True(filter.HasActiveFilter);
    }

    [Fact]
    public void Clear_ResetsEveryCriterion()
    {
        var filter = new HistoryFilterState(() => { })
        {
            FromDate = new DateTime(2026, 2, 1),
            ToDate = new DateTime(2026, 4, 1),
            MinDurationMinutes = 10
        };

        filter.ClearCommand.Execute(null);

        Assert.False(filter.HasActiveFilter);
        Assert.Null(filter.FromDate);
        Assert.Null(filter.ToDate);
        Assert.Null(filter.MinDurationMinutes);
    }

    [Fact]
    public void ToggleAndApply_OpenAndClosePopup()
    {
        var changes = 0;
        var filter = new HistoryFilterState(() => changes++);

        filter.ToggleCommand.Execute(null);
        Assert.True(filter.IsOpen);

        filter.ApplyCommand.Execute(null);
        Assert.False(filter.IsOpen);
        Assert.Equal(1, changes);
    }
}
