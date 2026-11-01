using System.Globalization;
using System.Text.Json;

namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class WorkerProtocolTests
{
    [Fact]
    public void Progress_RoundTripsThroughTheConsoleLine()
    {
        var line = WorkerProtocol.FormatProgress(42.5);

        Assert.Equal("@@P 42.5", line);
        Assert.True(WorkerProtocol.TryParseProgress(line, out var value));
        Assert.Equal(42.5, value);
    }

    [Fact]
    public void Progress_IsWrittenWithThreeDecimalsAtMost()
    {
        Assert.Equal("@@P 12.346", WorkerProtocol.FormatProgress(12.34567));
        Assert.Equal("@@P 100", WorkerProtocol.FormatProgress(100));
    }

    [Fact]
    public void Progress_IgnoresTheCultureOfTheProcess()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-ES");

        try
        {
            var line = WorkerProtocol.FormatProgress(0.75);

            Assert.Equal("@@P 0.75", line);
            Assert.True(WorkerProtocol.TryParseProgress(line, out var value));
            Assert.Equal(0.75, value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("42")]
    [InlineData("@@P ")]
    [InlineData("@@P cuarenta")]
    [InlineData("@@L {}")]
    [InlineData("whisper_init: loading model")]
    public void TryParseProgress_RejectsAnythingThatIsNotAProgressLine(string? line)
    {
        Assert.False(WorkerProtocol.TryParseProgress(line, out _));
    }

    [Fact]
    public void LiveMessage_RoundTripsThroughTheConsoleLine()
    {
        var sent = new LiveWorkerMessage
        {
            Id = 7,
            Ms = 1234,
            Text = "hello",
            Segments = [new SegmentDto { StartTicks = 10, EndTicks = 20, Text = "hola" }]
        };

        var line = WorkerProtocol.FormatLiveMessage(sent);

        Assert.StartsWith("@@L ", line);
        Assert.True(WorkerProtocol.IsLiveMessage(line));
        var received = WorkerProtocol.ParseLiveMessage(line);
        Assert.NotNull(received);
        Assert.Equal(7, received.Id);
        Assert.Equal(1234, received.Ms);
        Assert.Equal("hello", received.Text);
        Assert.False(received.Ready);
        Assert.Null(received.Error);
        var segment = Assert.Single(received.Segments!);
        Assert.Equal(10, segment.StartTicks);
        Assert.Equal(20, segment.EndTicks);
        Assert.Equal("hola", segment.Text);
    }

    [Fact]
    public void LiveMessage_CarriesReadyAndErrorSignals()
    {
        var ready = WorkerProtocol.ParseLiveMessage(WorkerProtocol.FormatLiveMessage(new LiveWorkerMessage { Ready = true }));
        var error = WorkerProtocol.ParseLiveMessage(WorkerProtocol.FormatLiveMessage(new LiveWorkerMessage { Error = "boom" }));

        Assert.True(ready!.Ready);
        Assert.Equal("boom", error!.Error);
        Assert.Equal(0, error.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@@P 50")]
    [InlineData("{\"Ready\":true}")]
    [InlineData(" @@L {}")]
    public void IsLiveMessage_RequiresThePrefixAtTheStart(string? line)
    {
        Assert.False(WorkerProtocol.IsLiveMessage(line));
    }

    [Fact]
    public void ParseLiveMessage_ThrowsOnACorruptPayload()
    {
        Assert.ThrowsAny<JsonException>(() => WorkerProtocol.ParseLiveMessage("@@L {no es json"));
    }
}
