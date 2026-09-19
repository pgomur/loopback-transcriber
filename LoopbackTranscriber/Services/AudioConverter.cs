using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LoopbackTranscriber.Services;

public static class AudioConverter
{
    public const int WhisperSampleRate = 16000;

    public static void ConvertToWhisperFormat(string sourceWavPath, string destWavPath)
    {
        using var reader = new AudioFileReader(sourceWavPath);
        ISampleProvider sampleProvider = reader;

        if (reader.WaveFormat.Channels > 1)
        {
            sampleProvider = new StereoToMonoSampleProvider(sampleProvider)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }

        if (reader.WaveFormat.SampleRate != WhisperSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, WhisperSampleRate);
        }

        WaveFileWriter.CreateWaveFile16(destWavPath, sampleProvider);
    }
}
