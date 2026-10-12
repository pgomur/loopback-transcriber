using System.Runtime.InteropServices;
using LoopbackTranscriber.Interop;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LoopbackTranscriber.Services;

public sealed class ProcessLoopbackRecorder : IDisposable
{
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private AutoResetEvent? _sampleEvent;
    private ManualResetEvent? _stopEvent;
    private Thread? _captureThread;
    private FileStream? _fileStream;
    private BinaryWriter? _writer;
    private long _dataBytesWritten;
    private WAVEFORMATEX _format;
    private Exception? _captureError;

    private byte[] _scratchBuffer = new byte[4096];

    private long _packetCount;
    private long _silentPacketCount;
    private long _framesCaptured;
    private int _peakSampleAbs;

    public string LastCaptureStats { get; private set; } = string.Empty;

    public bool IsRecording { get; private set; }

    public async Task StartAsync(uint processId, string outputWavPath, bool includeProcessTree = true)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException(Strings.Instance.Get("ErrorRecordingAlreadyRunning"));
        }

        _audioClient = await Wasapi.ActivateProcessLoopbackAsync(processId, includeProcessTree)
            .ConfigureAwait(false);

        _format = WAVEFORMATEX.CreatePlain(WAVEFORMATEX.WAVE_FORMAT_PCM, 44100, 2, 16);

        const AUDCLNT_STREAMFLAGS flags = AUDCLNT_STREAMFLAGS.LOOPBACK
            | AUDCLNT_STREAMFLAGS.EVENTCALLBACK
            | AUDCLNT_STREAMFLAGS.AUTOCONVERTPCM
            | AUDCLNT_STREAMFLAGS.SRC_DEFAULT_QUALITY;

        var hr = _audioClient.Initialize(AUDCLNT_SHAREMODE.Shared, flags, 0, 0, ref _format, IntPtr.Zero);
        ThrowIfFailed(hr, "IAudioClient.Initialize");

        _sampleEvent = new AutoResetEvent(false);
        hr = _audioClient.SetEventHandle(_sampleEvent.SafeWaitHandle.DangerousGetHandle());
        ThrowIfFailed(hr, "IAudioClient.SetEventHandle");

        var captureClientGuid = typeof(IAudioCaptureClient).GUID;
        hr = _audioClient.GetService(ref captureClientGuid, out var captureClientPtr);
        ThrowIfFailed(hr, "IAudioClient.GetService(IAudioCaptureClient)");
        _captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(captureClientPtr);
        Marshal.Release(captureClientPtr);

        var directory = Path.GetDirectoryName(outputWavPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _fileStream = new FileStream(outputWavPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new BinaryWriter(_fileStream);
        WriteWavHeaderPlaceholder(_writer, _format);
        _dataBytesWritten = 0;
        _packetCount = 0;
        _silentPacketCount = 0;
        _framesCaptured = 0;
        _peakSampleAbs = 0;
        LastCaptureStats = string.Empty;
        _captureError = null;
        _stopEvent = new ManualResetEvent(false);

        hr = _audioClient.Start();
        ThrowIfFailed(hr, "IAudioClient.Start");

        IsRecording = true;

        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "LoopbackTranscriber-Capture" };
        _captureThread.Start();
    }

    public async Task<string> StopAsync()
    {
        if (!IsRecording)
        {
            throw new InvalidOperationException(Strings.Instance.Get("ErrorNoRecordingRunning"));
        }

        IsRecording = false;
        _stopEvent!.Set();

        var path = await Task.Run(FinishStop).ConfigureAwait(false);

        var error = _captureError;
        _captureError = null;
        if (error is not null)
        {
            throw new InvalidOperationException(Strings.Instance.Get("ErrorAudioCapture"), error);
        }

        return path;
    }

    private string FinishStop()
    {
        var thread = _captureThread;
        thread?.Join(TimeSpan.FromSeconds(5));

        _audioClient?.Stop();

        FixWavHeader(_writer!, _dataBytesWritten, _format);
        _writer!.Flush();
        var path = _fileStream!.Name;

        _writer.Dispose();
        _fileStream.Dispose();
        _writer = null;
        _fileStream = null;

        ReleaseAudioObjects();

        _sampleEvent?.Dispose();
        _stopEvent?.Dispose();
        _sampleEvent = null;
        _stopEvent = null;

        LastCaptureStats =
            $"paquetes={_packetCount}, en_silencio={_silentPacketCount}, frames={_framesCaptured}, pico_senal={_peakSampleAbs}/32768";

        return path;
    }

    private void CaptureLoop()
    {
        try
        {
            var handles = new WaitHandle[] { _sampleEvent!, _stopEvent! };
            while (true)
            {
                var signaled = WaitHandle.WaitAny(handles, 2000);
                if (signaled == 1)
                {

                    DrainAvailablePackets();
                    break;
                }

                if (signaled != WaitHandle.WaitTimeout)
                {
                    DrainAvailablePackets();
                }
            }
        }
        catch (Exception ex)
        {
            _captureError = ex;
        }
    }

    private void EnsureScratchCapacity(int size)
    {
        if (_scratchBuffer.Length < size)
        {
            _scratchBuffer = new byte[size];
        }
    }

    private void DrainAvailablePackets()
    {
        while (true)
        {
            var hr = _captureClient!.GetNextPacketSize(out var framesAvailable);
            if (hr < 0 || framesAvailable == 0)
            {
                break;
            }

            hr = _captureClient.GetBuffer(out var dataPtr, out var numFrames, out var bufferFlags, out _, out _);
            if (hr < 0)
            {
                break;
            }

            var bytesToCapture = (int)(numFrames * _format.nBlockAlign);
            if (bytesToCapture > 0)
            {
                _packetCount++;
                _framesCaptured += numFrames;

                EnsureScratchCapacity(bytesToCapture);

                if ((bufferFlags & AUDCLNT_BUFFERFLAGS_SILENT) != 0 || dataPtr == IntPtr.Zero)
                {
                    _silentPacketCount++;
                    Array.Clear(_scratchBuffer, 0, bytesToCapture);
                    _writer!.Write(_scratchBuffer, 0, bytesToCapture);
                }
                else
                {
                    Marshal.Copy(dataPtr, _scratchBuffer, 0, bytesToCapture);
                    _writer!.Write(_scratchBuffer, 0, bytesToCapture);

                    for (var i = 0; i + 1 < bytesToCapture; i += 2)
                    {
                        var sample = (short)(_scratchBuffer[i] | (_scratchBuffer[i + 1] << 8));
                        var abs = Math.Abs((int)sample);
                        if (abs > _peakSampleAbs)
                        {
                            _peakSampleAbs = abs;
                        }
                    }
                }

                _dataBytesWritten += bytesToCapture;
            }

            _captureClient.ReleaseBuffer(numFrames);
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

    private void ReleaseAudioObjects()
    {
        if (_captureClient is not null)
        {
            Marshal.ReleaseComObject(_captureClient);
            _captureClient = null;
        }

        if (_audioClient is not null)
        {
            Marshal.ReleaseComObject(_audioClient);
            _audioClient = null;
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
        if (IsRecording)
        {
            try
            {
                IsRecording = false;
                _stopEvent?.Set();
                var abandonedPath = FinishStop();
                TryDeleteFile(abandonedPath);
            }
            catch (Exception ex)
            {
                AppLog.Warn("ProcessLoopbackRecorder.Dispose: fallo al detener la grabacion en curso", ex);
            }
        }

        _writer?.Dispose();
        _fileStream?.Dispose();
        _sampleEvent?.Dispose();
        _stopEvent?.Dispose();
        ReleaseAudioObjects();
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ProcessLoopbackRecorder: no se pudo borrar \"{path}\"", ex);
        }
    }
}
