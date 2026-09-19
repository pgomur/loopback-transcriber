using System.Diagnostics;
using System.Windows.Threading;
using LoopbackTranscriber.Models;
using System.Linq;

namespace LoopbackTranscriber.Services;

public sealed class TargetProcessStatus
{
    public required TargetApp App { get; init; }
    public bool IsRunning { get; set; }
    public uint ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
}

public sealed class TargetProcessWatcher : IDisposable
{
    private readonly DispatcherTimer _timer;

    public IReadOnlyList<TargetProcessStatus> Statuses { get; }

    public event EventHandler? StatusesChanged;

    public TargetProcessWatcher()
    {
        Statuses = TargetApp.All.Select(app => new TargetProcessStatus { App = app }).ToList();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    public void Refresh()
    {
        var changed = false;

        foreach (var status in Statuses)
        {
            uint foundPid = 0;
            var foundName = string.Empty;

            foreach (var candidateName in status.App.ProcessNames)
            {
                var processes = Process.GetProcessesByName(candidateName);
                if (processes.Length == 0)
                {
                    continue;
                }

                foundPid = (uint)processes[0].Id;
                foundName = candidateName;

                foreach (var p in processes)
                {
                    p.Dispose();
                }

                break;
            }

            var isRunning = foundPid != 0;
            if (isRunning != status.IsRunning || foundPid != status.ProcessId)
            {
                changed = true;
            }

            status.IsRunning = isRunning;
            status.ProcessId = foundPid;
            status.ProcessName = foundName;
        }

        if (changed)
        {
            StatusesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
