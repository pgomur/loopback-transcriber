using System.Text.Json;

namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeSettingsStore(AppSettings settings) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public AppSettings Current => settings;

    public int Updates { get; private set; }

    public AppSettings Snapshot() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)!;

    public void Update(Action<AppSettings> change)
    {
        change(settings);
        Updates++;
    }
}
