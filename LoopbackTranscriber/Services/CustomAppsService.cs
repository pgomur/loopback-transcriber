using System.Linq;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public interface ICustomAppsService
{
    IReadOnlyList<TargetApp> CustomApps { get; }

    event EventHandler? Changed;

    TargetApp AddApp(string processName, string displayName);

    void RemoveApp(string id);
}

public sealed class CustomAppsService : ICustomAppsService
{
    private readonly ISettingsStore _settingsService;
    private readonly List<TargetApp> _customApps;

    public IReadOnlyList<TargetApp> CustomApps => _customApps;

    public event EventHandler? Changed;

    public CustomAppsService(ISettingsStore settingsService)
    {
        _settingsService = settingsService;

        var settings = _settingsService.Snapshot();
        _customApps = settings.CustomApps
            .Select(saved => new TargetApp
            {
                Id = saved.Id,
                DisplayName = saved.DisplayName,
                ProcessNames = [saved.ProcessName]
            })
            .ToList();
    }

    public TargetApp AddApp(string processName, string displayName)
    {
        var id = "custom:" + processName.ToLowerInvariant();

        var existing = _customApps.FirstOrDefault(a => a.Id == id);
        if (existing is not null)
        {
            return existing;
        }

        var app = new TargetApp
        {
            Id = id,
            DisplayName = displayName,
            ProcessNames = [processName]
        };

        _customApps.Add(app);
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);

        return app;
    }

    public void RemoveApp(string id)
    {
        var index = _customApps.FindIndex(a => a.Id == id);
        if (index < 0)
        {
            return;
        }

        _customApps.RemoveAt(index);
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Persist()
    {
        var customApps = _customApps
            .Select(app => new CustomTargetAppSettings
            {
                Id = app.Id,
                DisplayName = app.DisplayName,
                ProcessName = app.ProcessNames[0]
            })
            .ToList();

        _settingsService.Update(settings => settings.CustomApps = customApps);
    }
}
