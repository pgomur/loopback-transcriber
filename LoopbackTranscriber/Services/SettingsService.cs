using System.Text.Json;
using LoopbackTranscriber.Models;
using System.IO;

namespace LoopbackTranscriber.Services;

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

public sealed class SettingsService : ISettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly object FileLock = new();

    public AppSettings Load()
    {
        lock (FileLock)
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            try
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch (JsonException ex)
            {
                AppLog.Warn($"SettingsService.Load: \"{SettingsPath}\" is corrupt; quarantining it and using defaults", ex);
                QuarantineCorruptFile();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"SettingsService.Load: could not read \"{SettingsPath}\"; using defaults", ex);
            }

            return new AppSettings();
        }
    }

    private static void QuarantineCorruptFile()
    {
        try
        {
            File.Move(SettingsPath, SettingsPath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("SettingsService: could not quarantine the corrupt settings file", ex);
        }
    }

    public void EnsureDefaultUserName()
    {
        var settings = Load();
        if (settings.UserNameInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.UserName))
        {
            settings.UserName = SystemUserName.Detect();
        }

        settings.UserNameInitialized = true;
        Save(settings);
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);

        lock (FileLock)
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
}
