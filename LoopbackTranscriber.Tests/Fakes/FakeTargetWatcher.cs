namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeTargetWatcher : ITargetWatcher
{
    public List<TargetProcessStatus> StatusList { get; } = [];

    public List<string> Added { get; } = [];

    public List<string> Removed { get; } = [];

    public bool Started { get; private set; }

    public IReadOnlyList<TargetProcessStatus> Statuses => StatusList;

    public event EventHandler? StatusesChanged;

    public void Start() => Started = true;

    public void AddTarget(TargetApp app)
    {
        Added.Add(app.Id);
        StatusList.Add(new TargetProcessStatus { App = app });
    }

    public void RemoveTarget(string appId)
    {
        Removed.Add(appId);
        StatusList.RemoveAll(s => s.App.Id == appId);
    }

    public void SetRunning(string appId, bool running, uint processId = 4321)
    {
        var status = StatusList.First(s => s.App.Id == appId);
        status.IsRunning = running;
        status.ProcessId = running ? processId : 0;
        StatusesChanged?.Invoke(this, EventArgs.Empty);
    }
}
