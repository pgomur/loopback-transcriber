using TranscriptSegment = LoopbackTranscriber.Services.WhisperTranscriptionService.TranscriptSegment;

namespace LoopbackTranscriber.Services;

public sealed record TranscriptionRequest(
    string WavPath,
    string ModelPath,
    string Language,
    string? FallbackLanguage,
    int ThreadCount,
    bool StrictNoSpeech,
    bool UseGpu = true);

public sealed record TrackTranscription(List<TranscriptSegment> Segments, string? Language);
