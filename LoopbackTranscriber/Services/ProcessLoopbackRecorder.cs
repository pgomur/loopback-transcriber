using System.Runtime.InteropServices;
using LoopbackTranscriber.Interop;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LoopbackTranscriber.Services;

public sealed class ProcessLoopbackRecorder : IProcessCapture
{
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    private const uint AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR = 0x4;

    public const int CaptureSampleRate = 44100;
    public const int CaptureChannels = 2;

    private sealed class CaptureSession
    {
        public required IAudioClient AudioClient;
        public required IAudioCaptureClient CaptureClient;
        public required AutoResetEvent SampleEvent;
        public required ManualResetEvent StopEvent;
        public required FileStream FileStream;
        public required BinaryWriter Writer;
        public required WAVEFORMATEX Format;
        public required CaptureTimeline Timeline;

        public readonly object WriteLock = new();
        public Thread? Thread;
        public volatile bool Abandoned;
        public volatile Exception? Error;
        public byte[] Scratch = new byte[4096];
        public long DataBytesWritten;
        public long PacketCount;
        public long SilentPacketCount;
        public long FramesCaptured;
        public int PeakSampleAbs;
    }

    private CaptureSession? _session;

    public string LastCaptureStats { get; private set; } = string.Empty;

    public double LastPeakAbs { get; private set; }

    public long? FirstSampleTimestamp { get; private set; }

    public event Action<byte[], int>? AudioCaptured;

    public bool IsRecording { get; private set; }

    public Task StartAsync(uint processId, string outputWavPath, bool includeProcessTree = true) =>
        Task.Run(() => StartOnMtaThreadAsync(processId, outputWavPath, includeProcessTree));

    private async Task StartOnMtaThreadAsync(uint processId, string outputWavPath, bool includeProcessTree)
    {
        if (IsRecording)
        {
            throw UserError.InvalidOperation("ErrorRecordingAlreadyRunning");
        }

        var audioClient = await Wasapi.ActivateProcessLoopbackAsync(processId, includeProcessTree)
            .ConfigureAwait(false);

        IAudioCaptureClient? captureClient = null;
        AutoResetEvent? sampleEvent = null;
        ManualResetEvent? stopEvent = null;
        FileStream? fileStream = null;
        BinaryWriter? writer = null;

        try
        {
            var format = WAVEFORMATEX.CreatePlain(WAVEFORMATEX.WAVE_FORMAT_PCM, CaptureSampleRate, CaptureChannels, 16);

            const AUDCLNT_STREAMFLAGS flags = AUDCLNT_STREAMFLAGS.LOOPBACK
                | AUDCLNT_STREAMFLAGS.EVENTCALLBACK
                | AUDCLNT_STREAMFLAGS.AUTOCONVERTPCM
                | AUDCLNT_STREAMFLAGS.SRC_DEFAULT_QUALITY;

            var hr = audioClient.Initialize(AUDCLNT_SHAREMODE.Shared, flags, 0, 0, ref format, IntPtr.Zero);
            ThrowIfFailed(hr, "IAudioClient.Initialize");

            sampleEvent = new AutoResetEvent(false);
            hr = audioClient.SetEventHandle(sampleEvent.SafeWaitHandle.DangerousGetHandle());
            ThrowIfFailed(hr, "IAudioClient.SetEventHandle");

            var captureClientGuid = typeof(IAudioCaptureClient).GUID;
            hr = audioClient.GetService(ref captureClientGuid, out var captureClientPtr);
            ThrowIfFailed(hr, "IAudioClient.GetService(IAudioCaptureClient)");
            captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(captureClientPtr);
            Marshal.Release(captureClientPtr);

            var directory = Path.GetDirectoryName(outputWavPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            fileStream = new FileStream(outputWavPath, FileMode.Create, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            writer = new BinaryWriter(fileStream);
            WriteWavHeaderPlaceholder(writer, format);
            stopEvent = new ManualResetEvent(false);

            var session = new CaptureSession
            {
                AudioClient = audioClient,
                CaptureClient = captureClient,
                SampleEvent = sampleEvent,
                StopEvent = stopEvent,
                FileStream = fileStream,
                Writer = writer,
                Format = format,
                Timeline = new CaptureTimeline(CaptureSampleRate)
            };

            hr = audioClient.Start();
            ThrowIfFailed(hr, "IAudioClient.Start");

            LastCaptureStats = string.Empty;
            LastPeakAbs = 0;
            FirstSampleTimestamp = null;
            _session = session;
            IsRecording = true;

            session.Thread = new Thread(() => CaptureLoop(session)) { IsBackground = true, Name = "LoopbackTranscriber-Capture" };
            session.Thread.Start();
        }
        catch
        {
            _session = null;
            IsRecording = false;
            TryDispose(writer);
            TryDispose(fileStream);
            TryDispose(sampleEvent);
            TryDispose(stopEvent);
            TryReleaseCom(captureClient);
            TryReleaseCom(audioClient);
            throw;
        }
    }

    public async Task<string> StopAsync()
    {
        if (!IsRecording || _session is null)
        {
            throw UserError.InvalidOperation("ErrorNoRecordingRunning");
        }

        var session = _session;
        IsRecording = false;
        session.StopEvent.Set();

        var path = await Task.Run(() => FinishStop(session)).ConfigureAwait(false);

        var error = session.Error;
        if (error is not null)
        {
            if (session.DataBytesWritten <= 0)
            {
                throw UserError.InvalidOperation(error, "ErrorAudioCapture");
            }

            AppLog.Warn("ProcessLoopbackRecorder: capture ended with an error; keeping the audio recorded so far", error);
        }

        return path;
    }

    private string FinishStop(CaptureSession session)
    {
        var thread = session.Thread;
        if (thread is not null && !thread.Join(TimeSpan.FromSeconds(5)))
        {
            AppLog.Info("ProcessLoopbackRecorder: capture thread is slow to finish; stopping the audio client");
            TryStop(session.AudioClient);

            if (!thread.Join(TimeSpan.FromSeconds(5)))
            {
                return AbandonStuckCapture(session);
            }
        }

        TryStop(session.AudioClient);

        string path;
        lock (session.WriteLock)
        {
            FixWavHeader(session.Writer, session.DataBytesWritten, session.Format);
            session.Writer.Flush();
            path = session.FileStream.Name;
            session.Writer.Dispose();
            session.FileStream.Dispose();
        }

        TryReleaseCom(session.CaptureClient);
        TryReleaseCom(session.AudioClient);
        session.SampleEvent.Dispose();
        session.StopEvent.Dispose();

        LastPeakAbs = session.PeakSampleAbs / 32768.0;
        FirstSampleTimestamp = session.Timeline.FirstSampleTimestamp;
        LastCaptureStats =
            $"packets={session.PacketCount}, silent={session.SilentPacketCount}, frames={session.FramesCaptured}, peak={session.PeakSampleAbs}/32768, gaps_filled={session.Timeline.PaddedDuration.TotalSeconds:0.0}s";
        AppLog.Info($"ProcessLoopbackRecorder: {LastCaptureStats}");

        if (ReferenceEquals(_session, session))
        {
            _session = null;
        }

        return path;
    }

    private string AbandonStuckCapture(CaptureSession session)
    {
        var path = session.FileStream.Name;
        AppLog.Error($"ProcessLoopbackRecorder: capture thread did not finish; abandoning the session (\"{path}\")");

        session.Abandoned = true;

        if (Monitor.TryEnter(session.WriteLock, TimeSpan.FromSeconds(2)))
        {
            try
            {
                FixWavHeader(session.Writer, session.DataBytesWritten, session.Format);
                session.Writer.Flush();
                session.Writer.Dispose();
                session.FileStream.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn("ProcessLoopbackRecorder: could not close the WAV of the abandoned session", ex);
            }
            finally
            {
                Monitor.Exit(session.WriteLock);
            }
        }
        else
        {
            try
            {
                using var headerStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete);
                var blockAlign = Math.Max(1, (int)session.Format.nBlockAlign);
                var dataBytes = Math.Max(0, headerStream.Length - 44);
                dataBytes -= dataBytes % blockAlign;
                using var headerWriter = new BinaryWriter(headerStream);
                FixWavHeader(headerWriter, dataBytes, session.Format);
            }
            catch (Exception ex)
            {
                AppLog.Warn("ProcessLoopbackRecorder: could not repair the header of the abandoned WAV", ex);
            }
        }

        LastPeakAbs = session.PeakSampleAbs / 32768.0;
        FirstSampleTimestamp = session.Timeline.FirstSampleTimestamp;
        LastCaptureStats = "capture abandoned: the capture thread did not respond";

        if (ReferenceEquals(_session, session))
        {
            _session = null;
        }

        return path;
    }

    private void CaptureLoop(CaptureSession session)
    {
        try
        {
            var handles = new WaitHandle[] { session.SampleEvent, session.StopEvent };
            while (!session.Abandoned)
            {
                var signaled = WaitHandle.WaitAny(handles, 2000);
                if (signaled == 1)
                {
                    DrainAvailablePackets(session);
                    break;
                }

                if (signaled != WaitHandle.WaitTimeout)
                {
                    DrainAvailablePackets(session);
                }
            }
        }
        catch (Exception ex)
        {
            session.Error = ex;
        }
    }

    private void DrainAvailablePackets(CaptureSession session)
    {
        var captureClient = session.CaptureClient;
        var blockAlign = session.Format.nBlockAlign;

        while (!session.Abandoned)
        {
            var hr = captureClient.GetNextPacketSize(out var framesAvailable);
            if (hr < 0 || framesAvailable == 0)
            {
                break;
            }

            hr = captureClient.GetBuffer(out var dataPtr, out var numFrames, out var bufferFlags, out _, out var qpcPosition);
            if (hr < 0)
            {
                break;
            }

            var bytesToCapture = (int)(numFrames * blockAlign);
            var written = false;
            if (bytesToCapture > 0)
            {
                lock (session.WriteLock)
                {
                    if (!session.Abandoned)
                    {
                        if (session.Scratch.Length < bytesToCapture)
                        {
                            session.Scratch = new byte[bytesToCapture];
                        }

                        session.PacketCount++;
                        session.FramesCaptured += numFrames;

                        if ((bufferFlags & AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR) == 0 && qpcPosition != 0)
                        {
                            var paddingFrames = session.Timeline.PaddingBefore((long)qpcPosition, numFrames);
                            if (paddingFrames > 0)
                            {
                                var paddingBytes = paddingFrames * blockAlign;
                                CaptureTimeline.WriteSilence(session.FileStream, paddingBytes);
                                session.DataBytesWritten += paddingBytes;
                            }
                        }
                        else
                        {
                            session.Timeline.AddUntimed(numFrames);
                        }

                        if ((bufferFlags & AUDCLNT_BUFFERFLAGS_SILENT) != 0 || dataPtr == IntPtr.Zero)
                        {
                            session.SilentPacketCount++;
                            Array.Clear(session.Scratch, 0, bytesToCapture);
                            session.Writer.Write(session.Scratch, 0, bytesToCapture);
                        }
                        else
                        {
                            Marshal.Copy(dataPtr, session.Scratch, 0, bytesToCapture);
                            session.Writer.Write(session.Scratch, 0, bytesToCapture);

                            for (var i = 0; i + 1 < bytesToCapture; i += 2)
                            {
                                var sample = (short)(session.Scratch[i] | (session.Scratch[i + 1] << 8));
                                var abs = Math.Abs((int)sample);
                                if (abs > session.PeakSampleAbs)
                                {
                                    session.PeakSampleAbs = abs;
                                }
                            }
                        }

                        session.DataBytesWritten += bytesToCapture;
                        written = true;
                    }
                }
            }

            if (written && !session.Abandoned)
            {
                RaiseAudioCaptured(session.Scratch, bytesToCapture);
            }

            captureClient.ReleaseBuffer(numFrames);
        }
    }

    private void RaiseAudioCaptured(byte[] buffer, int count)
    {
        var handler = AudioCaptured;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(buffer, count);
        }
        catch (Exception ex)
        {
            AppLog.Warn("ProcessLoopbackRecorder: a captured-audio subscriber failed", ex);
        }
    }

    private static void WriteWavHeaderPlaceholder(BinaryWriter writer, WAVEFORMATEX format)
    {
        writer.Write("RIFF"u8.ToArray());
        writer.Write(0);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write(format.wFormatTag);
        writer.Write(format.nChannels);
        writer.Write(format.nSamplesPerSec);
        writer.Write(format.nAvgBytesPerSec);
        writer.Write(format.nBlockAlign);
        writer.Write(format.wBitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(0);
    }

    private static void FixWavHeader(BinaryWriter writer, long dataBytesWritten, WAVEFORMATEX format)
    {
        var stream = writer.BaseStream;
        var dataSize = (uint)Math.Min(dataBytesWritten, uint.MaxValue);
        var riffSize = (uint)Math.Min(36 + dataBytesWritten, uint.MaxValue);

        stream.Seek(4, SeekOrigin.Begin);
        writer.Write(riffSize);

        stream.Seek(40, SeekOrigin.Begin);
        writer.Write(dataSize);

        stream.Seek(0, SeekOrigin.End);
    }

    private static void TryStop(IAudioClient client)
    {
        try
        {
            client.Stop();
        }
        catch (Exception ex)
        {
            AppLog.Warn("ProcessLoopbackRecorder: failed to stop the audio client", ex);
        }
    }

    private static void TryDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Warn("ProcessLoopbackRecorder: failed to release a resource", ex);
        }
    }

    private static void TryReleaseCom(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(comObject);
        }
        catch (Exception ex)
        {
            AppLog.Warn("ProcessLoopbackRecorder: failed to release a COM object", ex);
        }
    }

    private static void ThrowIfFailed(int hr, string context)
    {
        if (hr < 0)
        {
            throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException($"{context} fallo con HRESULT 0x{hr:X8}");
        }
    }

    public void Dispose()
    {
        var session = _session;
        if (IsRecording && session is not null)
        {
            try
            {
                IsRecording = false;
                session.StopEvent.Set();
                var keptPath = Task.Run(() => FinishStop(session)).GetAwaiter().GetResult();
                AppLog.Error($"ProcessLoopbackRecorder.Dispose: disposed with a recording in progress; the audio is kept unprocessed at \"{keptPath}\"");
            }
            catch (Exception ex)
            {
                AppLog.Warn("ProcessLoopbackRecorder.Dispose: failed to stop the recording in progress", ex);
            }
        }
    }
}
