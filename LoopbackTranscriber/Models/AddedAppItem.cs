namespace LoopbackTranscriber.Models;

public sealed class AddedAppItem
{
    public required TargetApp App { get; init; }

    public required bool CanRemove { get; init; }

    public bool IsBuiltIn { get; init; }

    public bool ShowRemoveButton => !IsBuiltIn;

    public string DisplayName => App.DisplayName;

    public string ProcessName => App.ProcessNames.FirstOrDefault() ?? string.Empty;
}
