using System.Globalization;
using System.IO;
namespace LoopbackTranscriber.Models;

public sealed class AppSettings
{

    public const string DefaultModelSize = "turbo";

    private string _modelSize = DefaultModelSize;

    public string ModelSize
    {
        get => _modelSize;
        set
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            _modelSize = normalized.Length == 0 ? DefaultModelSize : normalized;
        }
    }

    public string ModelsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "models");

    public string TranscriptsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "transcripts");

    public const string MeetingLanguageAuto = "auto";

    public string MeetingLanguage { get; set; } = string.Empty;

    public string ResolveMeetingLanguage() =>
        string.IsNullOrWhiteSpace(MeetingLanguage) ? UiLanguage : MeetingLanguage.Trim().ToLowerInvariant();

    public string UiLanguage { get; set; } = DetectSystemLanguage();

    public bool TranslucentUi { get; set; } = false;

    public string UserName { get; set; } = string.Empty;

    public bool UserNameInitialized { get; set; }

    public bool TrayHintShown { get; set; }

    public bool FirstRunSetupCompleted { get; set; }

    public List<CustomTargetAppSettings> CustomApps { get; set; } = [];

    public string LiveTranslationLanguage { get; set; } = string.Empty;

    public double LiveWindowOpacity { get; set; } = 1.0;

    public bool LiveShowOriginal { get; set; } = true;

    public bool LiveShowMetrics { get; set; } = true;

    public double LiveTextSize { get; set; } = 19;

    public int LiveAudioContext { get; set; }

    public bool LiveFlashAttention { get; set; }

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
