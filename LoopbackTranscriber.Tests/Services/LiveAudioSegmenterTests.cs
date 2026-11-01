namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class LiveAudioSegmenterTests
{
    private const int InputRate = ProcessLoopbackRecorder.CaptureSampleRate;
    private const int InputChannels = ProcessLoopbackRecorder.CaptureChannels;
    private const double FrameSeconds = 0.02;
    private const double Tolerance = 3 * FrameSeconds;

    [Fact]
    public void Silence_ProducesNoChunks_AndAVeryLowLevel()
    {
        var (segmenter, chunks) = Feed((3.0, 0));

        Assert.Empty(chunks);
        Assert.True(segmenter.LevelDb < -100, $"Level was {segmenter.LevelDb} dB");
    }

    [Fact]
    public void ASentenceFollowedByAPause_IsEmittedOnce_WithPreRollAndShortTail()
    {
        var (_, chunks) = Feed((1.0, 0), (1.0, 0.5), (1.0, 0));

        var final = Assert.Single(chunks, c => !c.IsDraft);
        Assert.InRange(final.StartSeconds, 0.8 - Tolerance, 0.8 + Tolerance);
        Assert.InRange(final.DurationSeconds, 1.4 - Tolerance, 1.4 + Tolerance);
        Assert.Equal((int)Math.Round(final.DurationSeconds * LiveAudioSegmenter.OutputSampleRate), final.Samples.Length);
    }

    [Fact]
    public void WhileSomeoneSpeaks_DraftsAreEmitted_AndTheFinalChunkSaysWhichDraftItReplaces()
    {
        var (_, chunks) = Feed((1.0, 0), (1.0, 0.5), (1.0, 0));

        var drafts = chunks.Where(c => c.IsDraft).ToList();
        var final = Assert.Single(chunks, c => !c.IsDraft);

        Assert.NotEmpty(drafts);
        Assert.Equal(drafts.Select((_, i) => i + 1), drafts.Select(d => d.DraftSeq));
        Assert.Equal(drafts[^1].DraftSeq, final.DraftSeq);
        Assert.True(chunks.IndexOf(final) > chunks.IndexOf(drafts[^1]));
    }

    [Fact]
    public void AVeryShortSound_IsNotEmitted()
    {
        var (_, chunks) = Feed((1.0, 0), (0.2, 0.5), (1.0, 0));

        Assert.Empty(chunks);
    }

    [Fact]
    public void UninterruptedSpeech_IsSplitBeforeTenSeconds()
    {
        var (segmenter, chunks) = Feed((0.5, 0), (12.0, 0.5), (1.0, 0));

        var finals = chunks.Where(c => !c.IsDraft).ToList();

        Assert.True(finals.Count >= 2, $"Expected the speech to be split, got {finals.Count} chunk(s)");
        Assert.All(finals, c => Assert.True(c.DurationSeconds <= 10.0 + FrameSeconds, $"Chunk of {c.DurationSeconds}s"));
        Assert.InRange(finals.Sum(c => c.DurationSeconds), 12.0 - Tolerance, 12.6 + Tolerance);
        Assert.True(finals[1].StartSeconds >= finals[0].StartSeconds + finals[0].DurationSeconds - FrameSeconds);
        Assert.True(segmenter.LevelDb < -30);
    }

    [Fact]
    public void AudioCanArriveInSmallPackets()
    {
        var bytes = WavFile.Pcm16(InputRate, InputChannels, (1.0, 0), (1.0, 0.5), (1.0, 0));
        var segmenter = new LiveAudioSegmenter();
        var chunks = new List<LiveAudioChunk>();

        for (var offset = 0; offset < bytes.Length; offset += 1764)
        {
            var count = Math.Min(1764, bytes.Length - offset);
            segmenter.Feed(bytes[offset..(offset + count)], count, chunks.Add);
        }

        var final = Assert.Single(chunks, c => !c.IsDraft);
        Assert.InRange(final.DurationSeconds, 1.4 - Tolerance, 1.4 + Tolerance);
    }

    private static (LiveAudioSegmenter Segmenter, List<LiveAudioChunk> Chunks) Feed(params (double Seconds, double Amplitude)[] parts)
    {
        var bytes = WavFile.Pcm16(InputRate, InputChannels, parts);
        var segmenter = new LiveAudioSegmenter();
        var chunks = new List<LiveAudioChunk>();

        segmenter.Feed(bytes, bytes.Length, chunks.Add);

        return (segmenter, chunks);
    }
}
