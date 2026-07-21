using Windows.Graphics.Capture;

namespace RecMode.Capture;

/// <summary>
/// Applies WGC session options shared by preview and recording: cursor capture (Win10 2004+) and yellow-
/// border suppression (Win11 only). Both are best-effort — older OSes lack the properties, so failures are
/// swallowed (the plan feature-gates border suppression; the border simply shows on Win10).
/// </summary>
internal static class CaptureSessionConfig
{
    public static void Apply(GraphicsCaptureSession session, bool captureCursor)
    {
        try
        {
            session.IsCursorCaptureEnabled = captureCursor;
        }
        catch (Exception)
        {
            // Property unavailable on this OS build.
        }

        try
        {
            // Win11-only (SDK 22000+); our TFM is 19041, so set it via reflection when the runtime has it.
            System.Reflection.PropertyInfo? borderProp =
                typeof(GraphicsCaptureSession).GetProperty("IsBorderRequired");
            borderProp?.SetValue(session, false);
        }
        catch (Exception)
        {
            // Win10: no such property; the border is shown.
        }
    }
}
