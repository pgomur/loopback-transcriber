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

    public static readonly TargetApp Discord = new()
    {
        Id = "discord",
        DisplayName = "Discord",
        ProcessNames = ["Discord", "DiscordCanary", "DiscordPTB"]
    };

    public static readonly TargetApp Zoom = new()
    {
        Id = "zoom",
        DisplayName = "Zoom",
        ProcessNames = ["Zoom"]
    };

    public static IReadOnlyList<TargetApp> All { get; } = [Teams, Discord, Zoom];
}
