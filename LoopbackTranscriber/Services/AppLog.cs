using System.Diagnostics;
using System.IO;
using System.Text;

namespace LoopbackTranscriber.Services;

internal static class AppLog
{
    private const long MaxFileBytes = 1_000_000;

    private static readonly object Gate = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "logs", "app.log");

    public static void Warn(string context, Exception ex) => Write("WARN", $"{context}: {ex}");

    public static void Error(string message) => Write("ERROR", message);

    public static void Info(string message) => Write("INFO", message);

    private static void Write(string level, string message)
    {
        Debug.WriteLine($"[LoopbackTranscriber] {level} {message}");

        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [pid {Environment.ProcessId}] {message}{Environment.NewLine}";

            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                RotateIfNeeded();

                using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                var bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            System.Diagnostics.Debug.WriteLine($"AppLog: could not write the log: {ex.Message}");
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxFileBytes)
        {
            return;
        }

        try
        {
            File.Move(LogPath, LogPath + ".1", overwrite: true);
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            System.Diagnostics.Debug.WriteLine($"AppLog: could not rotate the log: {ex.Message}");
        }
    }
}

internal sealed class WorkerStderrTail
{
    private const int MaxLines = 40;

    private readonly Queue<string> _lines = new();
    private readonly object _lock = new();

    public void Add(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_lock)
        {
            _lines.Enqueue(line);
            while (_lines.Count > MaxLines)
            {
                _lines.Dequeue();
            }
        }
    }

    public string Dump()
    {
        lock (_lock)
        {
            return _lines.Count == 0
                ? "(stderr empty)"
                : "worker stderr:\n" + string.Join("\n", _lines);
        }
    }
}
