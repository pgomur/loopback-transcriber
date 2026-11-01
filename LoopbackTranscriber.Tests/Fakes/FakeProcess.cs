namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeProcess : IProcessCapture
{
    public event Action<byte[], int>? AudioCaptured;

    public bool FailStart { get; set; }

    public bool FailStop { get; set; }

    public TaskCompletionSource? StartGate { get; set; }

    public int Starts { get; private set; }

    public bool Stopped { get; private set; }

    public string WavPath { get; private set; } = "";

    public double LastPeakAbs { get; set; } = 0.5;

    public long? FirstSampleTimestamp { get; set; }

    public async Task StartAsync(uint processId, string outputWavPath, bool includeProcessTree = true)
    {
        Starts++;
        if (FailStart)
        {
            throw new InvalidOperationException("no arranca");
        }

        WavPath = outputWavPath;

        if (StartGate is not null)
        {
            await StartGate.Task;
        }
    }

    public Task<string> StopAsync()
    {
        Stopped = true;
        if (FailStop)
        {
            throw new InvalidOperationException("no para");
        }

        if (Directory.Exists(Path.GetDirectoryName(WavPath)))
        {
            File.WriteAllBytes(WavPath, [1]);
        }

        return Task.FromResult(WavPath);
    }

    public void Raise(byte[] data) => AudioCaptured?.Invoke(data, data.Length);

    public void Dispose()
    {
    }
}
