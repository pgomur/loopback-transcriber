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

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                {
                    settings.Language = "auto";
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"SettingsService.Load: no se pudo leer \"{SettingsPath}\", se usaran valores por defecto", ex);
        }

        return new AppSettings();
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
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
