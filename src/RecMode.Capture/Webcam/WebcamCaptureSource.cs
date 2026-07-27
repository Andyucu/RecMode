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

    /// <summary>The camera's negotiated native resolution, known synchronously right after <see cref="StartAsync"/>
    /// returns (from the frame source's own <c>CurrentFormat</c>) — used by <see cref="WebcamPreviewEngine"/>,
    /// which needs a valid size immediately, before any actual frame has arrived.</summary>
    public int NativeWidth { get; private set; }
    public int NativeHeight { get; private set; }

    /// <summary>Raised (on the WinRT frame-reader thread) each time a new frame lands in the latest-frame
    /// buffer. Lets consumers block until there is genuinely something new instead of polling — the camera
    /// delivers at its own rate (typically 30 fps), which is usually well below the rate a recording's CFR
    /// pacer or a preview's refresh would otherwise re-read and re-convert the same unchanged pixels at.</summary>
    public event Action? FrameArrived;

    public async Task StartAsync(string deviceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        if (IsRunning)
        {
            await StopAsync().ConfigureAwait(false);
        }

        var mediaCapture = new MediaCapture();
        MediaFrameReader? frameReader = null;
        try
        {
            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = deviceId, StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly, MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            }).AsTask().ConfigureAwait(false);

            MediaFrameSource? colorSource = mediaCapture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (colorSource is null) throw new InvalidOperationException("Selected camera has no color video source.");

            NativeWidth = (int)colorSource.CurrentFormat.VideoFormat.Width;
            NativeHeight = (int)colorSource.CurrentFormat.VideoFormat.Height;

            frameReader = await mediaCapture.CreateFrameReaderAsync(colorSource, MediaEncodingSubtypes.Bgra8).AsTask().ConfigureAwait(false);
            frameReader.FrameArrived += OnFrameArrived;
            await frameReader.StartAsync().AsTask().ConfigureAwait(false);

            _mediaCapture = mediaCapture;
            _frameReader = frameReader;
            _hasFrame = false;
            IsRunning = true;
        }
        catch
        {
            if (frameReader is not null) { frameReader.FrameArrived -= OnFrameArrived; frameReader.Dispose(); }
            mediaCapture.Dispose();
            throw;
        }
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

        // Raised outside the lock: a subscriber that immediately calls TryGetLatestFrame would otherwise
        // re-enter _sync from this same thread, and any slow handler would stall the frame reader.
        FrameArrived?.Invoke();
    }

    public bool TryGetLatestFrame(ref byte[] destination, out int width, out int height, out int stride)
    {
        lock (_sync)
        {
            if (!_hasFrame || _latest is null)
            {
                width = height = stride = 0;
                return false;
            }

            width = _width;
            height = _height;
            stride = _width * 4;

            // Copy under the lock — see IWebcamFrameSource for why the caller must not get _latest itself.
            if (destination.Length < _latest.Length)
            {
                destination = new byte[_latest.Length];
            }
            Buffer.BlockCopy(_latest, 0, destination, 0, _latest.Length);
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

    /// <summary>Best-effort synchronous teardown for legacy call sites.</summary>
    public void Stop()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
