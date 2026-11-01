namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeMic : IMicCapture
{
    public bool FailStart { get; set; }

    public bool StartSucceeds { get; set; } = true;

    public bool FailStop { get; set; }

    public bool Stopped { get; private set; }

    public string? WavPath { get; private set; }

    public long? FirstSampleTimestamp { get; set; }

    public bool Start(string wavPath)
    {
        if (FailStart)
        {
            throw new InvalidOperationException("no microphone");
        }

        if (!StartSucceeds)
        {
            return false;
        }

        WavPath = wavPath;
        return true;
    }

    public string? Stop()
    {
        Stopped = true;
        if (FailStop)
        {
            throw new InvalidOperationException("no para");
        }

        if (WavPath is not null && Directory.Exists(Path.GetDirectoryName(WavPath)))
        {
            File.WriteAllBytes(WavPath, [1]);
        }

        return WavPath;
    }

    public void Dispose()
    {
    }
}
