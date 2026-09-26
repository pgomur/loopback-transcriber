namespace LoopbackTranscriber.Models;

public sealed class TargetApp
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    public required string[] ProcessNames { get; init; }

    public static readonly TargetApp Teams = new()
    {
        Id = "teams",
        DisplayName = "Teams",
        ProcessNames = ["ms-teams", "Teams"]
    };

    public static IReadOnlyList<TargetApp> All { get; } = [Teams];
}
