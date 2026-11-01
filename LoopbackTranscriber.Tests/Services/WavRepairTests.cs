namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class WavRepairTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void HasAudioData_IsTrueForAFileWithSamples()
    {
        Assert.True(WavRepair.HasAudioData(WavFile.WriteTone(_temp.Combine("tone.wav"), 0.1, 0.5)));
    }

    [Fact]
    public void HasAudioData_IsFalseForAFileWithOnlyTheHeader()
    {
        var path = WavFile.WriteSilence(_temp.Combine("empty.wav"), 0);

        Assert.False(WavRepair.HasAudioData(path));
    }

    [Fact]
    public void HasAudioData_IsFalseForAMissingFile()
    {
        Assert.False(WavRepair.HasAudioData(_temp.Combine("missing.wav")));
    }

    [Fact]
    public void HasAudioData_FallsBackToTheSizeWhenTheHeaderIsNotReadable()
    {
        Assert.True(WavRepair.HasAudioData(_temp.WriteBytes("garbage.wav", new byte[200])));
        Assert.False(WavRepair.HasAudioData(_temp.WriteBytes("tiny.wav", new byte[10])));
    }

    [Fact]
    public void IsInUse_IsTrueWhileAnotherHandleIsOpen()
    {
        var path = WavFile.WriteTone(_temp.Combine("busy.wav"), 0.1, 0.5);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.True(WavRepair.IsInUse(path));
        }

        Assert.False(WavRepair.IsInUse(path));
    }

    [Fact]
    public void TryRepairHeader_RestoresTheSizesOfARecordingCutShortByACrash()
    {
        var path = WavFile.WriteTone(_temp.Combine("crashed.wav"), 1.0, 0.5);
        var expectedDataSize = WavFile.ReadDataChunkSize(path);
        WavFile.BreakHeaderSizes(path);

        Assert.True(WavRepair.TryRepairHeader(path));

        Assert.Equal(expectedDataSize, WavFile.ReadDataChunkSize(path));
        Assert.Equal(new FileInfo(path).Length - 8, BitConverter.ToUInt32(File.ReadAllBytes(path), 4));
        Assert.InRange(WavFile.Duration(path).TotalSeconds, 0.999, 1.001);
    }

    [Fact]
    public void TryRepairHeader_IgnoresAHalfWrittenLastSample()
    {
        var path = WavFile.WriteTone(_temp.Combine("partial.wav"), 0.5, 0.5);
        var expectedDataSize = WavFile.ReadDataChunkSize(path);
        using (var stream = new FileStream(path, FileMode.Append))
        {
            stream.WriteByte(7);
        }

        WavFile.BreakHeaderSizes(path);

        Assert.True(WavRepair.TryRepairHeader(path));
        Assert.Equal(expectedDataSize, WavFile.ReadDataChunkSize(path));
    }

    [Fact]
    public void TryRepairHeader_LeavesAValidFileUntouched()
    {
        var path = WavFile.WriteTone(_temp.Combine("valid.wav"), 0.5, 0.5);
        var before = File.ReadAllBytes(path);

        Assert.True(WavRepair.TryRepairHeader(path));

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void TryRepairHeader_RejectsFilesThatAreNotWav()
    {
        Assert.False(WavRepair.TryRepairHeader(_temp.WriteText("notes.wav", new string('x', 100))));
        Assert.False(WavRepair.TryRepairHeader(_temp.WriteBytes("short.wav", new byte[10])));
        Assert.False(WavRepair.TryRepairHeader(_temp.Combine("missing.wav")));
    }

    [Fact]
    public void MeasurePeak_FindsTheLoudestSample()
    {
        var path = WavFile.WritePcm16(_temp.Combine("peak.wav"), 16000, 1, (0.2, 0.1), (0.2, 0.5));

        Assert.InRange(WavRepair.MeasurePeak(path, stopAt: 1.0), 0.49, 0.51);
    }

    [Fact]
    public void MeasurePeak_StopsAsSoonAsTheThresholdIsReached()
    {
        var path = WavFile.WriteTone(_temp.Combine("loud.wav"), 1.0, 0.5);

        var peak = WavRepair.MeasurePeak(path, stopAt: 0.01);

        Assert.InRange(peak, 0.01, 0.5);
    }

    [Fact]
    public void MeasurePeak_IsZeroForSilenceAndForUnreadableFiles()
    {
        Assert.Equal(0, WavRepair.MeasurePeak(WavFile.WriteSilence(_temp.Combine("silent.wav"), 0.5), 1.0));
        Assert.Equal(0, WavRepair.MeasurePeak(_temp.WriteText("broken.wav", "no es audio"), 1.0));
    }
}
