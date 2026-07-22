using System.Runtime.InteropServices;
using NAudio.Wave;

namespace RecMode.Audio.ProcessLoopback;

/// <summary>
/// Captures only the audio rendered by one process (and, by default, its child processes) — per-app audio
/// (plan §7/user's feature-triage list, Phase 7). Uses the WASAPI "process loopback" activation added in
/// Windows 10 2004 (build 19041), which NAudio doesn't expose, so this is raw COM interop against
/// <c>ActivateAudioInterfaceAsync</c> with <c>AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK</c>. Implements
/// <see cref="IWaveIn"/> so it plugs into <see cref="MixSource"/> exactly like the existing system/mic
/// captures — no changes needed to the mixing/metering code.
/// </summary>
public sealed class ProcessLoopbackCapture : IWaveIn
{
    private const string VirtualProcessLoopbackDevice = "VAD\\Process_Loopback";
    private const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const long DefaultBufferDurationHns = 200 * 10_000; // 200 ms, in 100-ns units
    private const uint TargetPid_Unused = 0;
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(5);

    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    private readonly uint _targetProcessId;
    private readonly bool _includeProcessTree;

    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private Thread? _pumpThread;
    private volatile bool _stopping;

    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public ProcessLoopbackCapture(int targetProcessId, bool includeProcessTree = true)
    {
        _targetProcessId = (uint)targetProcessId;
        _includeProcessTree = includeProcessTree;
    }

    public void StartRecording()
    {
        IntPtr activatedPtr = ActivateProcessLoopback(_targetProcessId, _includeProcessTree);
        try
        {
            _audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(activatedPtr);
        }
        finally
        {
            Marshal.Release(activatedPtr);
        }

        var format = new WAVEFORMATEX
        {
            FormatTag = 3, // WAVE_FORMAT_IEEE_FLOAT
            Channels = (ushort)WaveFormat.Channels,
            SamplesPerSec = (uint)WaveFormat.SampleRate,
            BitsPerSample = (ushort)WaveFormat.BitsPerSample,
            BlockAlign = (ushort)(WaveFormat.Channels * WaveFormat.BitsPerSample / 8),
            Size = 0,
        };
        format.AvgBytesPerSec = format.SamplesPerSec * format.BlockAlign;

        _audioClient.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK,
            DefaultBufferDurationHns, 0, ref format, IntPtr.Zero);

        _audioClient.GetService(typeof(IAudioCaptureClient).GUID, out object captureObj);
        _captureClient = (IAudioCaptureClient)captureObj;

        _stopping = false;
        _audioClient.Start();

        _pumpThread = new Thread(PumpLoop) { IsBackground = true, Name = "recmode-processloopback" };
        _pumpThread.Start();
    }

    private void PumpLoop()
    {
        Exception? failure = null;
        try
        {
            while (!_stopping)
            {
                _captureClient!.GetNextPacketSize(out uint framesAvailable);
                if (framesAvailable == 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                _captureClient.GetBuffer(out IntPtr dataPtr, out uint framesToRead, out uint flags, out _, out _);
                int bytes = (int)(framesToRead * WaveFormat.BlockAlign);
                if (bytes > 0)
                {
                    byte[] buffer = new byte[bytes];
                    const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
                    if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) == 0 && dataPtr != IntPtr.Zero)
                    {
                        Marshal.Copy(dataPtr, buffer, 0, bytes);
                    }
                    DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));
                }

                _captureClient.ReleaseBuffer(framesToRead);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            RecordingStopped?.Invoke(this, new StoppedEventArgs(failure));
        }
    }

    public void StopRecording()
    {
        _stopping = true;
        _pumpThread?.Join(1000);
        try { _audioClient?.Stop(); } catch (Exception) { }
    }

    public void Dispose()
    {
        StopRecording();
        if (_captureClient is not null)
        {
            Marshal.ReleaseComObject(_captureClient);
        }
        if (_audioClient is not null)
        {
            Marshal.ReleaseComObject(_audioClient);
        }
        _captureClient = null;
        _audioClient = null;
    }

    /// <summary>Activates the process-loopback virtual audio device, returning a raw (AddRef'd) <c>IAudioClient</c> pointer.</summary>
    private static IntPtr ActivateProcessLoopback(uint targetProcessId, bool includeProcessTree)
    {
        var activationParams = new AUDIOCLIENT_ACTIVATION_PARAMS
        {
            ActivationType = 1, // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
            ProcessLoopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
            {
                TargetProcessId = targetProcessId,
                ProcessLoopbackMode = includeProcessTree ? 0 : 1, // INCLUDE_TARGET_PROCESS_TREE : EXCLUDE_TARGET_PROCESS_TREE
            },
        };

        int blobSize = Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>();
        IntPtr blobPtr = Marshal.AllocHGlobal(blobSize);
        IntPtr propvariantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PROPVARIANT_BLOB>());
        try
        {
            Marshal.StructureToPtr(activationParams, blobPtr, false);
            var pv = new PROPVARIANT_BLOB { Vt = 65 /* VT_BLOB */, BlobSize = (uint)blobSize, BlobData = blobPtr };
            Marshal.StructureToPtr(pv, propvariantPtr, false);

            var handler = new ActivateCompletionHandler();
            int hr = ActivateAudioInterfaceAsync(VirtualProcessLoopbackDevice, IID_IAudioClient, propvariantPtr,
                handler, out IActivateAudioInterfaceAsyncOperation operation);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            if (!handler.Task.Wait(ActivationTimeout))
            {
                // Activation uses a system-owned async COM operation and has no cancellation API. Returning
                // promptly lets recording fall back to video-only rather than hanging on a broken audio service.
                throw new TimeoutException("Timed out while activating process-loopback audio.");
            }

            return handler.Task.GetAwaiter().GetResult();
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
            Marshal.FreeHGlobal(propvariantPtr);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
    {
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIOCLIENT_ACTIVATION_PARAMS
    {
        public int ActivationType;
        public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROPVARIANT_BLOB
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public uint BlobSize;
        [FieldOffset(16)] public IntPtr BlobData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(out int activateResult, out IntPtr activatedInterface);
    }

    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    private sealed class ActivateCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly TaskCompletionSource<IntPtr> _tcs = new();
        public Task<IntPtr> Task => _tcs.Task;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                activateOperation.GetActivateResult(out int hr, out IntPtr activatedInterface);
                if (hr < 0)
                {
                    try
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }
                    catch (Exception hrEx)
                    {
                        _tcs.TrySetException(hrEx);
                    }
                }
                else
                {
                    _tcs.TrySetResult(activatedInterface);
                }
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
        }
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, ref WAVEFORMATEX format, IntPtr audioSessionGuid);
        void GetBufferSize(out uint bufferFrameCount);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint currentPadding);
        void IsFormatSupported(int shareMode, ref WAVEFORMATEX format, out IntPtr closestMatch);
        void GetMixFormat(out IntPtr deviceFormat);
        void GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr eventHandle);
        void GetService([MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out uint numFramesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        void ReleaseBuffer(uint numFramesRead);
        void GetNextPacketSize(out uint numFramesInNextPacket);
    }
}
