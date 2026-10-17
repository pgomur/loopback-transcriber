using System.Text.Json;
using LoopbackTranscriber.Models;
using System.IO;

namespace LoopbackTranscriber.Services;

public sealed class SettingsService
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
                AppLog.Warn($"SettingsService.Load: \"{SettingsPath}\" esta corrupto, se aparta y se usan valores por defecto", ex);
                QuarantineCorruptFile();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"SettingsService.Load: no se pudo leer \"{SettingsPath}\", se usaran valores por defecto", ex);
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
            AppLog.Warn("SettingsService: no se pudo apartar el fichero de ajustes corrupto", ex);
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
