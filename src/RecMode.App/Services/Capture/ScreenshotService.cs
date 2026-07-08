using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using RecMode.Capture;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Core.Recording;
using RecMode.Core.Settings;

namespace RecMode.App.Services;

/// <summary>Captures a still (plan Phase 5): full-res PNG saved to the screenshots folder + copied to the clipboard.</summary>
public sealed class ScreenshotService(IAppPaths paths, ISettingsService settings, IErrorReporter errors)
{
    /// <summary>Raised (on the UI thread) with the saved path after a successful capture.</summary>
    public event Action<string>? Captured;

    /// <summary>Captures <paramref name="target"/> to a PNG. Must be called on the UI (STA) thread for the clipboard copy.</summary>
    public string? Capture(CaptureTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        ScreenshotImage? img;
        try
        {
            img = ScreenshotCapturer.Capture(target);
        }
        catch (Exception ex)
        {
            errors.Warn("screenshot.failed", "Couldn't capture a screenshot.", null, ex);
            return null;
        }

        if (img is null)
        {
            errors.Warn("screenshot.no-frame", "Couldn't capture a screenshot from the selected source.");
            return null;
        }

        var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, img.Bgra, img.Stride);
        bmp.Freeze();

        try
        {
            string dir = settings.Current.ScreenshotFolder ?? paths.ScreenshotsDirectory;
            Directory.CreateDirectory(dir);
            string name = FilenameBuilder.BuildFileName(
                settings.Current.FilenamePattern, DateTimeOffset.Now, "Screenshot", "", "png");
            string path = FilenameBuilder.BuildUniquePath(dir, name);

            using (FileStream fs = File.Create(path))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }

            TrySetClipboard(bmp);
            Captured?.Invoke(path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Warn("screenshot.save-failed", "Couldn't save the screenshot.",
                "Check that the screenshots folder is writable.", ex);
            return null;
        }
    }

    private static void TrySetClipboard(BitmapSource bmp)
    {
        try
        {
            Clipboard.SetImage(bmp);
        }
        catch (Exception)
        {
            // Clipboard can be transiently locked by another app; the file is still saved.
        }
    }
}
