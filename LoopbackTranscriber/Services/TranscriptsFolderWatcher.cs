using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LoopbackTranscriber.Services;

public sealed class TranscriptsFolderWatcher : IDisposable
{
    private readonly DispatcherTimer _debounce;
    private FileSystemWatcher? _watcher;
    private string? _watchedFolder;

    public TranscriptsFolderWatcher()
    {
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? Changed;

    public void Watch(string folder)
    {
        if (_watcher is not null &&
            string.Equals(_watchedFolder, folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _watcher?.Dispose();
        _watcher = null;
        _watchedFolder = folder;

        try
        {
            Directory.CreateDirectory(folder);

            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };

            watcher.Deleted += OnFolderChanged;
            watcher.Renamed += OnFolderChanged;
            watcher.Created += OnFolderChanged;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException || ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn($"TranscriptsFolderWatcher.Watch: could not watch \"{folder}\"", ex);
        }
    }

    private void OnFolderChanged(object? sender, FileSystemEventArgs e) => RestartDebounce(forgetFolder: false);

    private void OnWatcherError(object? sender, ErrorEventArgs e)
    {
        AppLog.Warn("TranscriptsFolderWatcher: the transcripts folder watcher failed; it will be restarted", e.GetException());
        RestartDebounce(forgetFolder: true);
    }

    private void RestartDebounce(bool forgetFolder)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (forgetFolder)
            {
                _watchedFolder = null;
            }

            _debounce.Stop();
            _debounce.Start();
        });
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }
}
