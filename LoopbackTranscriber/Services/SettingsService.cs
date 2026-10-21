using System.Text.Json;
using LoopbackTranscriber.Models;
using System.IO;

namespace LoopbackTranscriber.Services;

public interface ISettingsStore
{
    AppSettings Snapshot();

    void Update(Action<AppSettings> change);
}

public sealed class SettingsService : ISettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const int WriteAttempts = 3;
    private static readonly TimeSpan WriteRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly object _gate = new();
    private readonly UserNotices? _notices;
    private string _currentJson;
    private bool _pendingWrite;

    public SettingsService(UserNotices? notices = null)
    {
        _notices = notices;
        _currentJson = JsonSerializer.Serialize(ReadFromDisk(), JsonOptions);
    }

    public AppSettings Snapshot()
    {
        string json;
        lock (_gate)
        {
            json = _currentJson;
        }

        return Deserialize(json);
    }

    public void Update(Action<AppSettings> change)
    {
        lock (_gate)
        {
            var settings = Deserialize(_currentJson);
            change(settings);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            if (string.Equals(json, _currentJson, StringComparison.Ordinal) && !_pendingWrite)
            {
                return;
            }

            _currentJson = json;
            Persist(json);
        }
    }

    public void FlushPendingWrite()
    {
        lock (_gate)
        {
            if (_pendingWrite)
            {
                Persist(_currentJson);
            }
        }
    }

    private void Persist(string json)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                WriteToDisk(json);
                if (_pendingWrite)
                {
                    AppLog.Info("SettingsService: the pending settings were saved");
                }

                _pendingWrite = false;
                return;
            }
            catch (Exception ex) when (ExpectedFailures.IsFile(ex))
            {
                if (attempt < WriteAttempts)
                {
                    Thread.Sleep(WriteRetryDelay * attempt);
                    continue;
                }

                _pendingWrite = true;
                AppLog.Warn($"SettingsService: could not save \"{SettingsPath}\" after {WriteAttempts} attempts; keeping the changes in memory and retrying on the next change", ex);
                _notices?.AddOnce("settings-write", UserMessage.FromKey("NoticeSettingsNotSaved"));
                return;
            }
        }
    }

    public void EnsureDefaultUserName() => Update(settings =>
    {
        if (settings.UserNameInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.UserName))
        {
            settings.UserName = SystemUserName.Detect();
        }

        settings.UserNameInitialized = true;
    });

    private static AppSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

    private AppSettings ReadFromDisk()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            return Deserialize(File.ReadAllText(SettingsPath));
        }
        catch (JsonException ex)
        {
            AppLog.Warn($"SettingsService: \"{SettingsPath}\" is corrupt; quarantining it and using defaults", ex);
            QuarantineCorruptFile();
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn($"SettingsService: could not read \"{SettingsPath}\"; using defaults", ex);
        }

        _notices?.AddOnce("settings", UserMessage.FromKey("NoticeSettingsReset"));
        return new AppSettings();
    }

    private static void QuarantineCorruptFile()
    {
        try
        {
            File.Move(SettingsPath, SettingsPath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn("SettingsService: could not quarantine the corrupt settings file", ex);
        }
    }

    private static void WriteToDisk(string json)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(SettingsPath))
        {
            File.Replace(tempPath, SettingsPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, SettingsPath);
        }
    }
}
