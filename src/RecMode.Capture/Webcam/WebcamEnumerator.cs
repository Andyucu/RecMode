using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;

namespace RecMode.Capture.Webcam;

/// <summary>Enumerates available webcams (device class VideoCapture).</summary>
public static class WebcamEnumerator
{
    public static async Task<IReadOnlyList<WebcamDevice>> FindAllAsync()
    {
        DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask().ConfigureAwait(false);
        return devices.Select(d => new WebcamDevice(d.Id, d.Name)).ToList();
    }

    /// <summary>Synchronously (blocking) queries a webcam's negotiated native resolution — needed up front by
    /// <see cref="CaptureCapabilities.TryGetSourceSize"/> for a Webcam source, before <see cref="WebcamCaptureEngine"/>
    /// itself ever starts, since the encoder/ffmpeg pipe needs a fixed frame size decided before the first
    /// frame arrives. Briefly activates the camera (<c>SharedReadOnly</c>, same as <see cref="WebcamCaptureSource"/>,
    /// so this doesn't block a concurrent real session) just to read <c>CurrentFormat</c>, then releases it —
    /// a genuine ~100-300ms preflight cost specific to Webcam sources, documented rather than hidden.</summary>
    public static bool TryGetNativeResolution(string deviceId, out int width, out int height)
    {
        try
        {
            // MediaCapture's APIs are WinRT-async-only; running the probe on a threadpool thread and blocking
            // here (rather than blocking directly on the WinRT task) avoids a UI-thread marshaling deadlock,
            // since TryGetSourceSize is called synchronously from the UI thread during preflight.
            (int w, int h) = System.Threading.Tasks.Task.Run(async () =>
            {
                using var mediaCapture = new MediaCapture();
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
                    return (0, 0);
                }

                var format = colorSource.CurrentFormat.VideoFormat;
                return ((int)format.Width, (int)format.Height);
            }).GetAwaiter().GetResult();

            width = w;
            height = h;
            return width > 0 && height > 0;
        }
        catch (Exception)
        {
            width = height = 0;
            return false;
        }
    }
}
