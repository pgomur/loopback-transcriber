using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public static class LiveTranslationPreferences
{
    public const double MinOpacity = 0.3;
    public const double MinTextSize = 14;
    public const double MaxTextSize = 32;
    public const double DefaultTextSize = 19;

    private static readonly string[] SupportedLanguages = ["es", "en", "pt", "de", "ru"];

    public static double ClampOpacity(double value) =>
        double.IsNaN(value) ? 1.0 : Math.Clamp(value, MinOpacity, 1.0);

    public static double ClampTextSize(double value) =>
        double.IsNaN(value) || value <= 0 ? DefaultTextSize : Math.Clamp(Math.Round(value), MinTextSize, MaxTextSize);

    public static string ResolveLanguage(AppSettings settings, string uiLanguage)
    {
        var saved = settings.LiveTranslationLanguage;
        if (Array.IndexOf(SupportedLanguages, saved) >= 0)
        {
            return saved;
        }

        return Array.IndexOf(SupportedLanguages, uiLanguage) >= 0 ? uiLanguage : "en";
    }
}
