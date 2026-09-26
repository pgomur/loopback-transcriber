using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Interop;
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
    private readonly System.Threading.Timer _timer;
    private readonly Dispatcher _dispatcher;
    private readonly object _statusesLock = new();
    private readonly List<TargetProcessStatus> _statuses = [];
    private int _isRefreshing;
    private volatile bool _disposed;

    public IReadOnlyList<TargetProcessStatus> Statuses => _statuses;

    public event EventHandler? StatusesChanged;

    public TargetProcessWatcher()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new System.Threading.Timer(_ => RefreshTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        Refresh();
        _timer.Change(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1.5));
    }

    public void AddTarget(TargetApp app)
    {
        lock (_statusesLock)
        {
            if (_statuses.Any(s => s.App.Id == app.Id))
            {
                return;
            }

            _statuses.Add(new TargetProcessStatus { App = app });
        }

        RefreshTick();
    }

    public void RemoveTarget(string appId)
    {
        lock (_statusesLock)
        {
            var index = _statuses.FindIndex(s => s.App.Id == appId);
            if (index < 0)
            {
                return;
            }

            _statuses.RemoveAt(index);
        }

        RaiseStatusesChanged();
    }

    private void RefreshTick()
    {
        if (_disposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref _isRefreshing, 1) == 1)
        {
            return;
        }

        try
        {
            Refresh();
        }
        finally
        {
            Interlocked.Exchange(ref _isRefreshing, 0);
        }
    }

    public void Refresh()
    {
        TargetProcessStatus[] snapshot;
        lock (_statusesLock)
        {
            snapshot = [.. _statuses];
        }

        var changed = false;

        foreach (var status in snapshot)
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

                var pids = processes.Select(p => p.Id).ToArray();
                foundPid = ProcessTree.FindRootProcessId(pids);
                foundName = candidateName;

                foreach (var p in processes)
                {
                    p.Dispose();
                }

                break;
            }

            if (foundPid != 0)
            {
                var activePid = AudioSessionFinder.FindActiveProcessId(status.App.ProcessNames);
                if (activePid != 0)
                {
                    foundPid = activePid;
                }
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
            RaiseStatusesChanged();
        }
    }

    private void RaiseStatusesChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            StatusesChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dispatcher.BeginInvoke(() => StatusesChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
