namespace RecMode.Capture;

/// <summary>
/// Pure geometry behind live redaction (plan §7 privacy): maps a rect marked in <em>source</em> pixels
/// (monitor/window/virtual-desktop coordinates, the same space the capture texture lives in) into the
/// <em>output</em> pixels actually being blitted, through the current crop/zoom view rect, clamped to the
/// output bounds. Returns null when nothing of the marked rect would end up on screen.
/// <para>
/// Extracted from <see cref="VideoProcessorPipeline"/> purely so it is unit-testable: this sandbox cannot
/// create a D3D11 device at all, and this mapping is exactly the kind of arithmetic that fails silently —
/// a wrong offset leaves the secret visible, which is the one outcome a privacy feature must never produce.
/// </para>
/// </summary>
public static class RedactionMath
{
    /// <summary>Maps <paramref name="sourceRect"/> through <paramref name="viewRect"/> (the source-pixel
    /// rect currently being captured/scaled to the output, i.e. the crop plus any zoom) onto an
    /// <paramref name="outputWidth"/>×<paramref name="outputHeight"/> canvas. None of the inputs need to be
    /// in-bounds — both are intersected with the canvas and each other; null means the intersection is empty.</summary>
    public static RegionRect? MapToOutput(RegionRect sourceRect, RegionRect viewRect, int outputWidth, int outputHeight)
    {
        if (viewRect.Width <= 0 || viewRect.Height <= 0 || outputWidth <= 0 || outputHeight <= 0)
        {
            return null;
        }

        double scaleX = (double)outputWidth / viewRect.Width;
        double scaleY = (double)outputHeight / viewRect.Height;

        int left = (int)Math.Round((sourceRect.X - viewRect.X) * scaleX);
        int top = (int)Math.Round((sourceRect.Y - viewRect.Y) * scaleY);
        int right = (int)Math.Round((sourceRect.X + sourceRect.Width - viewRect.X) * scaleX);
        int bottom = (int)Math.Round((sourceRect.Y + sourceRect.Height - viewRect.Y) * scaleY);

        left = Math.Clamp(left, 0, outputWidth);
        right = Math.Clamp(right, 0, outputWidth);
        top = Math.Clamp(top, 0, outputHeight);
        bottom = Math.Clamp(bottom, 0, outputHeight);

        return right > left && bottom > top
            ? new RegionRect(left, top, right - left, bottom - top)
            : null;
    }
}
