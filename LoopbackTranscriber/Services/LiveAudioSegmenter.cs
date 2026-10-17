namespace LoopbackTranscriber.Services;

internal sealed record LiveAudioChunk(short[] Samples, double StartSeconds, double DurationSeconds, DateTime SpeechEndedAtUtc, bool IsDraft = false, int DraftSeq = 0);

internal sealed class LiveAudioSegmenter
{
    public const int OutputSampleRate = 16000;

    private const double InputSampleRate = ProcessLoopbackRecorder.CaptureSampleRate;
    private const double Ratio = InputSampleRate / OutputSampleRate;

    private const int FrameSamples = 320;
    private const double FrameSeconds = 0.02;
    private const double SpeechThresholdDb = -42.0;
    private const int PreRollFrames = 10;
    private const int PauseFrames = 30;
    private const int ShortPauseFrames = 15;
    private const int LongChunkFrames = 150;
    private const int MaxChunkFrames = 500;
    private const int DraftIntervalFrames = 40;
    private const int MinDraftSpeechFrames = 25;
    private const int CoveredSpeechTolerance = 3;
    private const int ClosingDraftSilentFrames = 8;
    private const int DraftMaxSilentRun = 10;
    private const int MinSpeechFrames = 20;
    private const int TailFrames = 10;
    private const int SplitSearchFrames = 150;

    private sealed class Frame
    {
        public required short[] Samples;
        public required double Db;
        public required bool Speech;
    }

    private double _sum;
    private double _filled;
    private short[] _frame = new short[FrameSamples];
    private int _frameFill;
    private double _frameSumSquares;

    private long _frameIndex;
    private readonly Queue<Frame> _preRoll = new();
    private List<Frame> _chunk = new();
    private bool _inSpeech;
    private long _chunkStartFrame;
    private int _speechFrames;
    private int _silentRun;
    private int _framesSinceDraft;
    private int _draftSeq;
    private int _lastDraftSeq;
    private int _speechFramesAtLastDraft;

    private double _levelDb = -120;

    public double LevelDb => _levelDb;

    public void Feed(byte[] buffer, int count, Action<LiveAudioChunk> onChunk)
    {
        for (var i = 0; i + 3 < count; i += 4)
        {
            var left = (short)(buffer[i] | (buffer[i + 1] << 8));
            var right = (short)(buffer[i + 2] | (buffer[i + 3] << 8));
            PushMonoSample((left + right) * 0.5, onChunk);
        }
    }

    private void PushMonoSample(double x, Action<LiveAudioChunk> onChunk)
    {
        var space = Ratio - _filled;
        if (space > 1.0)
        {
            _sum += x;
            _filled += 1.0;
            return;
        }

        _sum += x * space;
        var output = _sum / Ratio;
        var rest = 1.0 - space;
        _sum = x * rest;
        _filled = rest;

        var sample = (short)Math.Clamp(Math.Round(output), short.MinValue, short.MaxValue);
        _frame[_frameFill++] = sample;
        _frameSumSquares += (double)sample * sample;

        if (_frameFill == FrameSamples)
        {
            var rms = Math.Sqrt(_frameSumSquares / FrameSamples) / 32768.0;
            var db = 20.0 * Math.Log10(rms + 1e-9);
            ProcessFrame(new Frame { Samples = _frame, Db = db, Speech = db > SpeechThresholdDb }, onChunk);

            _frame = new short[FrameSamples];
            _frameFill = 0;
            _frameSumSquares = 0;
        }
    }

    private void ProcessFrame(Frame frame, Action<LiveAudioChunk> onChunk)
    {
        _levelDb = Math.Max(frame.Db, _levelDb - 0.6);

        if (!_inSpeech)
        {
            _preRoll.Enqueue(frame);
            if (_preRoll.Count > PreRollFrames)
            {
                _preRoll.Dequeue();
            }

            if (frame.Speech)
            {
                _inSpeech = true;
                _chunk = new List<Frame>(_preRoll);
                _chunkStartFrame = _frameIndex - (_preRoll.Count - 1);
                _preRoll.Clear();
                _speechFrames = 1;
                _silentRun = 0;
                _framesSinceDraft = 0;
                _lastDraftSeq = 0;
                _speechFramesAtLastDraft = 0;
            }
        }
        else
        {
            _chunk.Add(frame);
            _framesSinceDraft++;
            if (frame.Speech)
            {
                _speechFrames++;
                _silentRun = 0;
            }
            else
            {
                _silentRun++;
            }

            var pauseNeeded = _chunk.Count >= LongChunkFrames ? ShortPauseFrames : PauseFrames;
            if (_silentRun >= pauseNeeded)
            {
                EmitAtPause(onChunk);
            }
            else if (_chunk.Count >= MaxChunkFrames)
            {
                EmitSplit(onChunk);
            }
            else if (_speechFrames >= MinDraftSpeechFrames
                     && ((_framesSinceDraft >= DraftIntervalFrames && _silentRun < DraftMaxSilentRun)
                         || (_silentRun == ClosingDraftSilentFrames
                             && _speechFrames - _speechFramesAtLastDraft > CoveredSpeechTolerance)))
            {
                _framesSinceDraft = 0;
                _draftSeq++;
                _lastDraftSeq = _draftSeq;
                _speechFramesAtLastDraft = _speechFrames;
                onChunk(BuildChunk(_chunk, 0, _chunk.Count, _chunkStartFrame, DateTime.UtcNow, isDraft: true, draftSeq: _draftSeq));
            }
        }

        _frameIndex++;
    }

    private void EmitAtPause(Action<LiveAudioChunk> onChunk)
    {
        var keepSilence = Math.Min(_silentRun, TailFrames);
        var end = _chunk.Count - _silentRun + keepSilence;

        if (_speechFrames >= MinSpeechFrames)
        {
            var speechEndedAt = DateTime.UtcNow - TimeSpan.FromSeconds(_silentRun * FrameSeconds);
            var covered = _lastDraftSeq > 0 && _speechFrames - _speechFramesAtLastDraft <= CoveredSpeechTolerance
                ? _lastDraftSeq
                : 0;
            onChunk(BuildChunk(_chunk, 0, end, _chunkStartFrame, speechEndedAt, draftSeq: covered));
        }

        _lastDraftSeq = 0;

        _chunk = new List<Frame>();
        _inSpeech = false;
        _speechFrames = 0;
        _silentRun = 0;
        _framesSinceDraft = 0;
    }

    private void EmitSplit(Action<LiveAudioChunk> onChunk)
    {
        var from = Math.Max(1, _chunk.Count - SplitSearchFrames);
        var to = _chunk.Count - 10;
        var cut = to;
        var minDb = double.MaxValue;
        for (var i = from; i < to; i++)
        {
            if (_chunk[i].Db < minDb)
            {
                minDb = _chunk[i].Db;
                cut = i + 1;
            }
        }

        var speechEndedAt = DateTime.UtcNow - TimeSpan.FromSeconds((_chunk.Count - cut) * FrameSeconds);
        onChunk(BuildChunk(_chunk, 0, cut, _chunkStartFrame, speechEndedAt));

        var remainder = _chunk.GetRange(cut, _chunk.Count - cut);
        _chunk = remainder;
        _chunkStartFrame += cut;
        _speechFrames = remainder.Count(f => f.Speech);
        _framesSinceDraft = 0;
        _lastDraftSeq = 0;
        _speechFramesAtLastDraft = 0;

        _silentRun = 0;
        for (var i = remainder.Count - 1; i >= 0 && !remainder[i].Speech; i--)
        {
            _silentRun++;
        }
    }

    private static LiveAudioChunk BuildChunk(List<Frame> frames, int start, int end, long startFrame, DateTime speechEndedAt, bool isDraft = false, int draftSeq = 0)
    {
        var count = end - start;
        var samples = new short[count * FrameSamples];
        for (var i = 0; i < count; i++)
        {
            Array.Copy(frames[start + i].Samples, 0, samples, i * FrameSamples, FrameSamples);
        }

        return new LiveAudioChunk(samples, startFrame * FrameSeconds, count * FrameSeconds, speechEndedAt, isDraft, draftSeq);
    }
}
