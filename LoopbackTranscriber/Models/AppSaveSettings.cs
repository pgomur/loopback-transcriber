namespace LoopbackTranscriber.Models;

public sealed class AppSaveSettings
{
    public bool SaveAudio { get; set; } = true;

    public bool SaveTxt { get; set; } = true;

    public bool SaveMarkdown { get; set; } = true;
}
