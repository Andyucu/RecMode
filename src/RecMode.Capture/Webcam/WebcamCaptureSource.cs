using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace RecMode.Capture.Webcam;

/// <summary>
/// Captures a webcam via Windows.Media.Capture's frame-reader pipeline (BGRA8, CPU-side — most consumer
/// UVC webcams don't expose D3D surfaces through this API), publishing the latest frame for
/// <see cref="WebcamOverlayCompositor"/> to upload to the GPU on its own cadence. Runs only while the
/// Record screen is visible or a recording using it is active (§3.9) — start/stop are explicit, no
/// background polling. <see cref="MediaCaptureSharingMode.SharedReadOnly"/> lets the preview's instance and
/// a recording's instance run concurrently without fighting over the device.
/// </summary>
public sealed class WebcamCaptureSource : IWebcamFrameSource
{
    private readonly Lock _sync = new();
    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private byte[]? _latest;
    private int _width;
    private int _height;
    private bool _hasFrame;

    public bool IsRunning { get; private set; }

    public async Task StartAsync(string deviceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        if (IsRunning)
        {
            await StopAsync().ConfigureAwait(false);
        }

        var mediaCapture = new MediaCapture();
        await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            VideoDeviceId = deviceId,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            SharingMode = MediaCaptureSharingMode.SharedReadOnly,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        }).AsTask().ConfigureAwait(false);

        MediaFrameSource? colorSource = mediaCapture.FrameSources.Values
            .FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
        if (colorSource is null)
        {
            mediaCapture.Dispose();
            throw new InvalidOperationException("Selected camera has no color video source.");
        }

        MediaFrameReader frameReader = await mediaCapture
            .CreateFrameReaderAsync(colorSource, MediaEncodingSubtypes.Bgra8).AsTask().ConfigureAwait(false);
        frameReader.FrameArrived += OnFrameArrived;
        await frameReader.StartAsync().AsTask().ConfigureAwait(false);

        _mediaCapture = mediaCapture;
        _frameReader = frameReader;
        _hasFrame = false;
        IsRunning = true;
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using MediaFrameReference? frame = sender.TryAcquireLatestFrame();
        SoftwareBitmap? bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null)
        {
            return;
        }

        using BitmapBuffer buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using IMemoryBufferReference reference = buffer.CreateReference();
        BitmapPlaneDescription plane = buffer.GetPlaneDescription(0);

        unsafe
        {
            ((IMemoryBufferByteAccess)reference).GetBuffer(out byte* dataPtr, out uint _);

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int rowBytes = width * 4;

            lock (_sync)
            {
                if (_latest is null || _width != width || _height != height)
                {
                    _latest = new byte[rowBytes * height];
                    _width = width;
                    _height = height;
                }

                fixed (byte* dst = _latest)
                {
                    for (int y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(dataPtr + (long)(plane.StartIndex + y * plane.Stride), dst + (long)y * rowBytes, rowBytes, rowBytes);
                    }
                }

                _hasFrame = true;
            }
        }
    }

    public bool TryGetLatestFrame(out byte[] data, out int width, out int height, out int stride)
    {
        lock (_sync)
        {
            if (!_hasFrame || _latest is null)
            {
                data = [];
                width = height = stride = 0;
                return false;
            }

            data = _latest;
            width = _width;
            height = _height;
            stride = _width * 4;
            return true;
        }
    }

    public async Task StopAsync()
    {
        IsRunning = false;

        if (_frameReader is not null)
        {
            _frameReader.FrameArrived -= OnFrameArrived;
            try
            {
                await _frameReader.StopAsync().AsTask().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort teardown — the device may already be gone (unplugged mid-session).
            }
            _frameReader.Dispose();
            _frameReader = null;
        }

        _mediaCapture?.Dispose();
        _mediaCapture = null;

        lock (_sync)
        {
            _hasFrame = false;
        }
    }

    /// <summary>Fire-and-forget teardown for synchronous call sites (mirrors the other capture engines' <c>Stop()</c>).</summary>
    public void Stop()
    {
        _ = StopAsync();
    }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
