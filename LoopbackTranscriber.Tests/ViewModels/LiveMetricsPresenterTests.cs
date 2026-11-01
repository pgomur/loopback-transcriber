using System.Globalization;

namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class LiveMetricsPresenterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void SignalOnly_CombinedEqualsTheSignalText()
    {
        var view = LiveMetricsPresenter.Format(-30, null, null, Culture);

        Assert.Contains("-30 dB", view.Signal);
        Assert.Equal(view.Signal, view.Combined);
        Assert.Equal(string.Empty, view.Chunk);
        Assert.Equal(string.Empty, view.Translation);
    }

    [Fact]
    public void VeryLowLevel_IsShownAsNoSignal()
    {
        var view = LiveMetricsPresenter.Format(-80, null, null, Culture);

        Assert.Contains(Strings.Instance.Get("LiveWindowNoSignal"), view.Signal);
        Assert.DoesNotContain("dB", view.Signal);
    }

    [Fact]
    public void ChunkAndTranslation_AreAppendedWithSeparators()
    {
        var chunk = new LiveChunkMetrics(AudioSeconds: 2, WhisperSeconds: 0.5, LatencySeconds: 0.7, Backlog: 0, Dropped: 0, TextChars: 10);
        var translation = new LiveTranslationMetrics(Chars: 12, Seconds: 0.3, LatencySeconds: 0.4, Backlog: 0);

        var view = LiveMetricsPresenter.Format(-30, chunk, translation, Culture);

        Assert.Contains("2.0", view.Chunk);
        Assert.Contains("0.5", view.Chunk);
        Assert.Contains("0.3", view.Translation);
        Assert.Equal($"{view.Signal} · {view.Chunk} · {view.Translation}", view.Combined);
    }

    [Fact]
    public void Numbers_UseTheGivenCulture()
    {
        var chunk = new LiveChunkMetrics(2, 0.5, 0.7, 0, 0, 10);

        var view = LiveMetricsPresenter.Format(-30, chunk, null, new CultureInfo("es-ES"));

        Assert.Contains("2,0", view.Chunk);
        Assert.Contains("0,5", view.Chunk);
    }
}
