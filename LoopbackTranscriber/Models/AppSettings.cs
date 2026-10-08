using System.Globalization;
using System.IO;
namespace LoopbackTranscriber.Models;

public sealed class AppSettings
{

    public string ModelSize { get; set; } = "turbo";

    public string ModelsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "models");

    public string TranscriptsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "transcripts");

    public string Language { get; set; } = "auto";

    public string UiLanguage { get; set; } = DetectSystemLanguage();

    public bool TranslucentUi { get; set; } = true;

    public string UserName { get; set; } = string.Empty;

    public bool FirstRunSetupCompleted { get; set; }

    public List<CustomTargetAppSettings> CustomApps { get; set; } = [];

    public string GetModelFileName() => $"ggml-{ModelSize}.bin";

    public string GetModelFilePath() => Path.Combine(ModelsFolder, "whisper", GetModelFileName());

    public const string SummaryModelFileName = "Qwen3-4B-Q4_K_M.gguf";

    public string GetSummaryModelFilePath() => Path.Combine(ModelsFolder, "llm", SummaryModelFileName);

    private static string DetectSystemLanguage()
    {
        var systemCode = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToLowerInvariant();

        return systemCode switch
        {
            "es" => "es",
            "en" => "en",
            "pt" => "pt",
            "de" => "de",
            "ru" => "ru",
            _ => "en"
        };
    }
}
