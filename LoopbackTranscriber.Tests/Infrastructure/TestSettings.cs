namespace LoopbackTranscriber.Tests.Infrastructure;

public static class TestSettings
{
    public static AppSettings In(string root, string userName = "") => new()
    {
        TranscriptsFolder = Path.Combine(root, "transcripts"),
        ModelsFolder = Path.Combine(root, "models"),
        UiLanguage = "es",
        UserName = userName
    };

    public static void CreateModelFiles(AppSettings settings)
    {
        foreach (var path in new[] { settings.GetModelFilePath(), settings.GetSummaryModelFilePath() })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1]);
        }
    }
}
