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

    /// <summary>Captures <paramref name="target"/> to a PNG. Safe to call from any thread — the clipboard
    /// copy (the one step that needs an STA thread) is internally marshaled to the UI thread's dispatcher
    /// rather than requiring the caller to already be on it, so this can run off the UI thread via
    /// <c>Task.Run</c> (see <see cref="RecordViewModel.TakeScreenshot"/>) without blocking it for the whole
    /// capture+PNG-encode+file-write duration.</summary>
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
            string dir = paths.ResolveUserPath(settings.Current.ScreenshotFolder) ?? paths.ScreenshotsDirectory;
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

    // Marshaled to the UI/STA thread via BeginInvoke rather than called inline: Clipboard.SetImage requires
    // an STA thread, but Capture() itself no longer requires one — RecordViewModel.TakeScreenshot now runs
    // Capture() via Task.Run (a thread-pool/MTA thread) so the UI thread isn't blocked for the whole capture+
    // PNG-encode+file-write duration, which used to freeze the recording toolbar and every low-level input
    // hook behind it for a full-resolution 4K/ultrawide grab. This is the one remaining piece of the method
    // that genuinely needs the UI thread.
    private static void TrySetClipboard(BitmapSource bmp) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Clipboard.SetImage(bmp);
            }
            catch (Exception)
            {
                // Clipboard can be transiently locked by another app; the file is still saved.
            }
        });
}
