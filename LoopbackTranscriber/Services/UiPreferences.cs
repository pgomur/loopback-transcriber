namespace LoopbackTranscriber.Services;

public static class UiPreferences
{
    public static event EventHandler<bool>? TranslucentUiChanged;

    public static void NotifyTranslucentUiChanged(bool enabled) => TranslucentUiChanged?.Invoke(null, enabled);
}
