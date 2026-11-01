namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeCustomApps : ICustomAppsService
{
    private readonly List<TargetApp> _apps = [];

    public IReadOnlyList<TargetApp> CustomApps => _apps;

    public event EventHandler? Changed;

    public TargetApp AddApp(string processName, string displayName)
    {
        var app = new TargetApp
        {
            Id = "custom:" + processName.ToLowerInvariant(),
            DisplayName = displayName,
            ProcessNames = [processName]
        };
        _apps.Add(app);
        Changed?.Invoke(this, EventArgs.Empty);
        return app;
    }

    public void RemoveApp(string id)
    {
        _apps.RemoveAll(a => a.Id == id);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
