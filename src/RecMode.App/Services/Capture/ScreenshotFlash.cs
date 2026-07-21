using RecMode.Capture;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Services;

/// <summary>Shows the brief post-screenshot flash overlay (design: <c>rm-flash</c>).</summary>
public interface IScreenshotFlash
{
    void Flash(MonitorInfo monitor);
}

/// <summary>Default <see cref="IScreenshotFlash"/> — a transient <see cref="Views.ScreenshotFlashWindow"/>.</summary>
public sealed class ScreenshotFlash(IOsCapabilities os) : IScreenshotFlash
{
    public void Flash(MonitorInfo monitor)
    {
        var window = new Views.ScreenshotFlashWindow(monitor, os);
        window.Show();
    }
}
