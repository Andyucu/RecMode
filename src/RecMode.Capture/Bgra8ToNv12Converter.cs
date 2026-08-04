namespace RecMode.Capture;

/// <summary>
/// CPU BGRA8 → tightly-packed NV12 conversion (nearest-neighbor scale to the destination size, box-filtered
/// 2×2 chroma average). Shared by the two capture engines with no GPU VideoProcessor pass available to do
/// this on the GPU instead: <see cref="GdiCaptureEngine"/> (the WGC-unavailable fallback) and
/// <see cref="Webcam.WebcamCaptureEngine"/> (webcam-as-source — a webcam's modest resolution/frame rate make
/// a CPU conversion perfectly adequate, so this doesn't need its own D3D11 device).
/// </summary>
public static class Bgra8ToNv12Converter
{
    // BT.601 limited/"TV" range (luma 16-235, chroma 16-240) — the range every decoder assumes by default
    // absent an explicit signal in the bitstream, which is exactly what this pipeline leaves unset
    // (FfmpegArgsBuilder emits no -color_range flag at all — confirmed by grep, not just assumed; there is no
    // corresponding declaration to keep in sync if this ever changes). This used to be full-range (luma
    // 0-255, chroma centered at 128 over the full 0-255 span) with nothing signaling that either, so every
    // player expanded the actual 16-235/16-240 range a limited-range assumption implies right back out —
    // crushing near-black content to pure black and near-white content to pure white on both paths that use
    // this converter (the GDI VM/RDP fallback and webcam-as-source). If -color_range (or an equivalent
    // -colorspace/-color_trc flag) is ever added to FfmpegArgsBuilder, it must declare "tv"/limited to match
    // what's actually produced here — anyone doing that should not assume it already does.
    // ReadOnlySpan<byte>, not byte[]: lets GdiCaptureEngine read directly from the native DIB section pointer
    // (via an unsafe span) instead of Marshal.Copy-ing the whole frame into a managed array first purely so
    // this method could index it — 248.8 MB/s of pure memcpy at 1080p30, 884 MB/s at a large virtual desktop,
    // for a copy nothing but this method ever reads. Existing byte[] callers (WebcamCaptureEngine, the test
    // suite) are unaffected — arrays convert to spans implicitly.
    public static void Convert(ReadOnlySpan<byte> bgra, int srcW, int srcH, int dstW, int dstH, byte[] output)
    {
        int ySize = dstW * dstH;
        for (int y = 0; y < dstH; y++)
        for (int x = 0; x < dstW; x++)
        {
            (byte b, byte g, byte r) = Pixel(bgra, srcW, srcH, dstW, dstH, x, y);
            output[y * dstW + x] = (byte)Math.Clamp(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16, 0, 255);
        }
        for (int y = 0; y < dstH; y += 2)
        for (int x = 0; x < dstW; x += 2)
        {
            int sumU = 0, sumV = 0;
            for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
            {
                (byte b, byte g, byte r) = Pixel(bgra, srcW, srcH, dstW, dstH, x + dx, y + dy);
                sumU += ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                sumV += ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
            }
            int at = ySize + (y / 2) * dstW + x;
            output[at] = (byte)Math.Clamp(sumU / 4, 0, 255);
            output[at + 1] = (byte)Math.Clamp(sumV / 4, 0, 255);
        }
    }

    private static (byte B, byte G, byte R) Pixel(ReadOnlySpan<byte> p, int w, int h, int dstW, int dstH, int x, int y)
    {
        int sx = Math.Min(w - 1, x * w / dstW), sy = Math.Min(h - 1, y * h / dstH);
        int i = (sy * w + sx) * 4;
        return (p[i], p[i + 1], p[i + 2]);
    }
}
