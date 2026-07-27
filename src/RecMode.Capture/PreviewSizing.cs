namespace RecMode.Capture;

/// <summary>
/// Fits a capture source into the preview surface. Shared by both preview engines — <see cref="WgcPreviewEngine"/>
/// and <see cref="Webcam.WebcamPreviewEngine"/> had character-for-character identical private copies of this,
/// differing only in field names, and neither was testable.
/// <para>The even-dimension guarantee is the load-bearing part: NV12 and the D3D11 scaler both assume even
/// width and height, so an odd result would be a corrupt or over-running buffer rather than a slightly wrong
/// size.</para>
/// </summary>
public static class PreviewSizing
{
    /// <summary>Scales <paramref name="srcW"/>×<paramref name="srcH"/> down to fit inside
    /// <paramref name="maxW"/>×<paramref name="maxH"/>, preserving aspect ratio, never upscaling past the
    /// source's own size, and always returning even dimensions of at least 2.</summary>
    public static (int Width, int Height) Fit(int srcW, int srcH, int maxW, int maxH)
    {
        srcW = Math.Max(1, srcW);
        srcH = Math.Max(1, srcH);

        double scale = Math.Min(1.0, Math.Min(maxW / (double)srcW, maxH / (double)srcH));
        int w = Math.Max(2, (int)Math.Round(srcW * scale));
        int h = Math.Max(2, (int)Math.Round(srcH * scale));
        return (MakeEven(w), MakeEven(h));
    }

    private static int MakeEven(int value) => value % 2 == 0 ? value : Math.Max(2, value - 1);
}
