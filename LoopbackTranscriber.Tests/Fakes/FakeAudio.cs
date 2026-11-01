namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeAudio : IAudioPreparer
{
    public double DurationSeconds { get; set; } = 60;

    public bool[]? ActivityMap { get; set; }

    public double[]? LevelMap { get; set; }

    public bool NoiseGateFails { get; set; }

    public List<string> Converted { get; } = [];

    public List<string> NoiseGated { get; } = [];

    public void ConvertToWhisperFormat(string sourceWavPath, string destWavPath)
    {
        Converted.Add(destWavPath);
        File.WriteAllBytes(destWavPath, [1]);
    }

    public void ApplyNoiseGate(string wavPath)
    {
        if (NoiseGateFails)
        {
            throw new InvalidDataException("cabecera rota");
        }

        NoiseGated.Add(wavPath);
    }

    public bool[]? GetActivityMap(string wavPath) => ActivityMap;

    public double[]? GetLevelMap(string wavPath) => LevelMap;

    public double GetDurationSeconds(string wavPath) => DurationSeconds;
}
