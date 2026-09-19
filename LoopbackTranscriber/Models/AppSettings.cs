using System.IO;
namespace LoopbackTranscriber.Models;

public sealed class AppSettings
{

    public string ModelSize { get; set; } = "small";

    public string ModelsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "models");

    public string TranscriptsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "Transcripciones");

    public string Language { get; set; } = "es";

    public bool AutoSaveTranscripts { get; set; } = true;

    public string GetModelFileName() => $"ggml-{ModelSize}.bin";

    public string GetModelFilePath() => Path.Combine(ModelsFolder, GetModelFileName());
}
