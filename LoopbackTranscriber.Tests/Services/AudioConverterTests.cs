namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class AudioConverterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(48000, 2, true)]
    [InlineData(44100, 2, false)]
    [InlineData(48000, 1, false)]
    [InlineData(16000, 1, false)]
    public void ConvertToWhisperFormat_Produces16kHzMono16Bit_WithTheSameDuration(int sampleRate, int channels, bool floatSamples)
    {
        var source = floatSamples
            ? WavFile.WriteFloat(_temp.Combine("source.wav"), 1.0, 0.5, sampleRate, channels)
            : WavFile.WriteTone(_temp.Combine("source.wav"), 1.0, 0.5, sampleRate, channels);
        var destination = _temp.Combine("source.16k.wav");

        AudioConverter.ConvertToWhisperFormat(source, destination);

        var format = WavFile.Format(destination);
        Assert.Equal(AudioConverter.WhisperSampleRate, format.SampleRate);
        Assert.Equal(1, format.Channels);
        Assert.Equal(16, format.BitsPerSample);
        Assert.InRange(WavFile.Duration(destination).TotalSeconds, 0.98, 1.02);
    }

    [Fact]
    public void ConvertToWhisperFormat_AveragesBothChannels_WithoutChangingTheLevel()
    {
        var source = WavFile.WriteTone(_temp.Combine("stereo.wav"), 1.0, 0.5, 16000, 2);
        var destination = _temp.Combine("mono.wav");

        AudioConverter.ConvertToWhisperFormat(source, destination);

        var peak = WavFile.ReadPcm16(destination).Max(s => Math.Abs((int)s)) / (double)short.MaxValue;
        Assert.InRange(peak, 0.47, 0.53);
    }

    [Fact]
    public void GetLevelMap_HasOneValuePerFiftyMilliseconds_AndFollowsTheSignal()
    {
        var path = WavFile.WritePcm16(_temp.Combine("levels.wav"), 16000, 1, (0.5, 0), (0.5, 0.5));

        var levels = AudioConverter.GetLevelMap(path);

        Assert.NotNull(levels);
        Assert.Equal(20, levels.Length);
        Assert.All(levels.Take(10), rms => Assert.Equal(0, rms));
        var expectedRms = 0.5 * short.MaxValue / Math.Sqrt(2);
        Assert.All(levels.Skip(10), rms => Assert.InRange(rms, expectedRms * 0.97, expectedRms * 1.03));
    }

    [Fact]
    public void GetLevelMap_CountsAShortLastWindow()
    {
        var path = WavFile.WriteTone(_temp.Combine("short.wav"), 0.12, 0.5);

        Assert.Equal(3, AudioConverter.GetLevelMap(path)!.Length);
    }

    [Fact]
    public void GetLevelMap_OnlyReadsMono16BitFiles()
    {
        var stereo = WavFile.WriteTone(_temp.Combine("stereo.wav"), 0.5, 0.5, 16000, 2);
        var floats = WavFile.WriteFloat(_temp.Combine("float.wav"), 0.5, 0.5, 16000, 1);

        Assert.Null(AudioConverter.GetLevelMap(stereo));
        Assert.Null(AudioConverter.GetLevelMap(floats));
        Assert.Null(AudioConverter.GetActivityMap(stereo));
    }

    [Fact]
    public void GetActivityMap_MarksTheWindowsWithVoice()
    {
        var path = WavFile.WritePcm16(_temp.Combine("activity.wav"), 16000, 1, (0.1, 0.5), (0.1, 0));

        Assert.Equal(new[] { true, true, false, false }, AudioConverter.GetActivityMap(path));
    }

    [Fact]
    public void MeasureActivity_GivesTheActiveFractionAndSeconds()
    {
        bool[] map = [true, true, false, false];

        var (fraction, activeSeconds) = AudioConverter.MeasureActivity(map, TimeSpan.Zero, TimeSpan.FromMilliseconds(200));

        Assert.Equal(0.5, fraction);
        Assert.Equal(0.1, activeSeconds, precision: 6);
    }

    [Fact]
    public void MeasureActivity_OutsideTheMap_AssumesActivity()
    {
        bool[] map = [false, false];

        var (fraction, activeSeconds) = AudioConverter.MeasureActivity(map, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6));

        Assert.Equal(1.0, fraction);
        Assert.Equal(1.0, activeSeconds, precision: 6);
    }

    [Fact]
    public void ApplyNoiseGate_SilencesFaintNoise_AndKeepsTheVoiceUntouched()
    {
        var path = WavFile.WritePcm16(_temp.Combine("mic.wav"), 16000, 1, (0.5, 0.5), (0.5, 0.001), (0.5, 0.5));
        var before = WavFile.ReadPcm16(path);

        AudioConverter.ApplyNoiseGate(path);

        var after = WavFile.ReadPcm16(path);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before[1600..6400], after[1600..6400]);
        Assert.All(after[9600..14400], sample => Assert.Equal(0, sample));
        Assert.Equal(before[17600..22400], after[17600..22400]);
    }

    [Fact]
    public void ApplyNoiseGate_LeavesFilesItCannotGateAsTheyAre()
    {
        var floats = WavFile.WriteFloat(_temp.Combine("float.wav"), 0.5, 0.5, 16000, 1);
        var silent = WavFile.WriteSilence(_temp.Combine("silent.wav"), 0.5);
        var floatBytes = File.ReadAllBytes(floats);
        var silentBytes = File.ReadAllBytes(silent);

        AudioConverter.ApplyNoiseGate(floats);
        AudioConverter.ApplyNoiseGate(silent);

        Assert.Equal(floatBytes, File.ReadAllBytes(floats));
        Assert.Equal(silentBytes, File.ReadAllBytes(silent));
    }

    [Fact]
    public void NaudioAudioPreparer_ReadsTheDuration_AndReturnsZeroForABrokenFile()
    {
        var preparer = new NaudioAudioPreparer();
        var good = WavFile.WriteTone(_temp.Combine("good.wav"), 1.5, 0.5);
        var broken = _temp.WriteText("broken.wav", "no es un wav");

        Assert.Equal(1.5, preparer.GetDurationSeconds(good), precision: 3);
        Assert.Equal(0, preparer.GetDurationSeconds(broken));
    }
}
