namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeDevice : IDeviceCapture
{
    public bool FailStart { get; set; }

    public bool FailStop { get; set; }

    public bool Stopped { get; private set; }

    public List<DeviceLoopbackRecorder.DeviceTrack> Tracks { get; } = [];

    public void Start(string outputDirectory, string baseFileName)
    {
        if (FailStart)
        {
            throw new InvalidOperationException("sin dispositivo");
        }
    }

    public List<DeviceLoopbackRecorder.DeviceTrack> Stop()
    {
        Stopped = true;
        return FailStop ? throw new InvalidOperationException("no para") : Tracks;
    }

    public void Dispose()
    {
    }
}
