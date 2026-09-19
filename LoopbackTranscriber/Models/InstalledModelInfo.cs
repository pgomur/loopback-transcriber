namespace LoopbackTranscriber.Models;

public sealed class InstalledModelInfo
{
    public required string ModelSize { get; init; }
    public required string FilePath { get; init; }
    public required long FileSizeBytes { get; init; }
    public required string FileSizeText { get; init; }
    public required bool IsActive { get; init; }

    public string DisplayName => ModelSize switch
    {
        "tiny" => "Tiny",
        "base" => "Base",
        "small" => "Small",
        "medium" => "Medium",
        "large" => "Large",
        _ => ModelSize
    };
}
