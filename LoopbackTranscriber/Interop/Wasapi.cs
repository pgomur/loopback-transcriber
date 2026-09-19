using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LoopbackTranscriber.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct WAVEFORMATEX
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;

    public const ushort WAVE_FORMAT_PCM = 1;

    public static WAVEFORMATEX CreatePcm(int sampleRate, int channels, int bitsPerSample)
    {
        var wf = new WAVEFORMATEX
        {
            wFormatTag = WAVE_FORMAT_PCM,
            nChannels = (ushort)channels,
            nSamplesPerSec = (uint)sampleRate,
            wBitsPerSample = (ushort)bitsPerSample,
            cbSize = 0
        };
        wf.nBlockAlign = (ushort)(channels * bitsPerSample / 8);
        wf.nAvgBytesPerSec = wf.nSamplesPerSec * wf.nBlockAlign;
        return wf;
    }
}

internal enum AUDCLNT_SHAREMODE
{
    Shared = 0,
    Exclusive = 1
}

[Flags]
internal enum AUDCLNT_STREAMFLAGS : uint
{
    None = 0,
    CROSSPROCESS = 0x00010000,
    LOOPBACK = 0x00020000,
    EVENTCALLBACK = 0x00040000,
    NOPERSIST = 0x00080000,
    RATEADJUST = 0x00100000,
    SRC_DEFAULT_QUALITY = 0x08000000,
    AUTOCONVERTPCM = 0x80000000
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(AUDCLNT_SHAREMODE shareMode, AUDCLNT_STREAMFLAGS streamFlags, long hnsBufferDuration,
        long hnsPeriodicity, ref WAVEFORMATEX pFormat, IntPtr audioSessionGuid);

    [PreserveSig]
    int GetBufferSize(out uint numBufferFrames);

    [PreserveSig]
    int GetStreamLatency(out long hnsLatency);

    [PreserveSig]
    int GetCurrentPadding(out uint numPaddingFrames);

    [PreserveSig]
    int IsFormatSupported(AUDCLNT_SHAREMODE shareMode, ref WAVEFORMATEX pFormat, out IntPtr closestMatch);

    [PreserveSig]
    int GetMixFormat(out IntPtr deviceFormat);

    [PreserveSig]
    int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int SetEventHandle(IntPtr eventHandle);

    [PreserveSig]
    int GetService(ref Guid riid, out IntPtr ppv);
}

[ComImport]
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig]
    int GetBuffer(out IntPtr data, out uint numFramesToRead, out uint flags, out ulong devicePosition,
        out ulong qpcPosition);

    [PreserveSig]
    int ReleaseBuffer(uint numFramesRead);

    [PreserveSig]
    int GetNextPacketSize(out uint numFramesInNextPacket);
}

[ComImport]
[Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    [PreserveSig]
    int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
}

[ComImport]
[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    [PreserveSig]
    int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
}

internal static class Wasapi
{

    public const string VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK = "VAD\\Process_Loopback";

    public const int AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK = 1;
    public const int PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE = 0;
    public const int PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE = 1;

    private const ushort VT_BLOB = 65;

    [DllImport("Mmdevapi.dll", CallingConvention = CallingConvention.StdCall, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    public static async Task<IAudioClient> ActivateProcessLoopbackAsync(uint targetProcessId, bool includeProcessTree)
    {

        var pActivationParams = Marshal.AllocHGlobal(12);
        var pPropVariant = IntPtr.Zero;
        try
        {
            Marshal.WriteInt32(pActivationParams, 0, AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK);
            Marshal.WriteInt32(pActivationParams, 4, (int)targetProcessId);
            Marshal.WriteInt32(pActivationParams, 8, includeProcessTree
                ? PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
                : PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE);

            var propVariantSize = IntPtr.Size == 8 ? 24 : 16;
            pPropVariant = Marshal.AllocHGlobal(propVariantSize);
            for (var i = 0; i < propVariantSize; i++)
            {
                Marshal.WriteByte(pPropVariant, i, 0);
            }

            Marshal.WriteInt16(pPropVariant, 0, unchecked((short)VT_BLOB));
            Marshal.WriteInt32(pPropVariant, 8, 12);
            if (IntPtr.Size == 8)
            {
                Marshal.WriteInt64(pPropVariant, 16, pActivationParams.ToInt64());
            }
            else
            {
                Marshal.WriteInt32(pPropVariant, 12, pActivationParams.ToInt32());
            }

            var handler = new ActivateCompletionHandler();
            var riid = typeof(IAudioClient).GUID;

            var hr = ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, ref riid, pPropVariant,
                handler, out _);
            Marshal.ThrowExceptionForHR(hr);

            await handler.Completion.Task.ConfigureAwait(false);

            Marshal.ThrowExceptionForHR(handler.ActivateResult);

            return (IAudioClient)handler.ActivatedInterface!;
        }
        finally
        {
            Marshal.FreeHGlobal(pActivationParams);
            if (pPropVariant != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pPropVariant);
            }
        }
    }

    private sealed class ActivateCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        public readonly TaskCompletionSource Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ActivateResult { get; private set; } = -1;
        public object? ActivatedInterface { get; private set; }

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                var hr = activateOperation.GetActivateResult(out var activateResult, out var activatedInterface);
                ActivateResult = hr >= 0 ? activateResult : hr;
                if (hr >= 0)
                {
                    ActivatedInterface = activatedInterface;
                }
            }
            finally
            {
                Completion.TrySetResult();
            }

            return 0;
        }
    }
}
