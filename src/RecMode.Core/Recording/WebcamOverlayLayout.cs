using RecMode.Core.Settings;

namespace RecMode.Core.Recording;

/// <summary>
/// Pure geometry for the webcam picture-in-picture overlay: where the box sits within the output frame,
/// given a size percentage and corner. No GPU/D3D dependency — the compositor (RecMode.Capture) just needs
/// the resulting rectangle.
/// </summary>
public static class WebcamOverlayLayout
{
    private const double MarginFraction = 0.02;
    private const int MinSizePercent = 10;
    private const int MaxSizePercent = 50;

    /// <summary>Computes the PIP box (even-numbered dimensions, 16:9) within a <paramref name="outputWidth"/>×<paramref name="outputHeight"/> frame.</summary>
    public static (int X, int Y, int Width, int Height) ComputeRect(
        int outputWidth, int outputHeight, int sizePercent, WebcamOverlayPosition position)
    {
        sizePercent = Math.Clamp(sizePercent, MinSizePercent, MaxSizePercent);

        int width = outputWidth * sizePercent / 100;
        width -= width % 2;
        int height = width * 9 / 16;
        height -= height % 2;

        int margin = (int)(outputWidth * MarginFraction);
        margin -= margin % 2;

        int x = position is WebcamOverlayPosition.BottomRight or WebcamOverlayPosition.TopRight
            ? outputWidth - width - margin
            : margin;
        int y = position is WebcamOverlayPosition.BottomLeft or WebcamOverlayPosition.BottomRight
            ? outputHeight - height - margin
            : margin;

        return (Math.Max(0, x), Math.Max(0, y), width, height);
    }
}
