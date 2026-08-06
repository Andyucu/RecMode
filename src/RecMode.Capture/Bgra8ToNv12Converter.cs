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

        // Precompute the nearest-neighbor source column/row for every destination column/row ONCE, instead
        // of recomputing "x*srcW/dstW" / "y*srcH/dstH" (two integer divisions each) per sampled pixel. The
        // previous version paid this on every luma sample AND again on every chroma sub-sample — the same
        // (x,y) positions sampled twice, since the chroma loop re-called Pixel() for the identical 2x2 block
        // the luma loop had just read. On this project's own 2-hour soak (5120x1440, GDI fallback), that was
        // roughly 442M Pixel() calls/sec and ~884M divisions/sec — a previously-unattributed share of the
        // measured CPU (see PROJECT_MEMORY 2026-08-05 for the full accounting). The loop below fixes both:
        // one table build instead of per-pixel division, and one fetch per 2x2 block instead of two.
        int[] sxFor = new int[dstW];
        int[] syFor = new int[dstH];
        for (int x = 0; x < dstW; x++) sxFor[x] = Math.Min(srcW - 1, x * srcW / dstW);
        for (int y = 0; y < dstH; y++) syFor[y] = Math.Min(srcH - 1, y * srcH / dstH);

        // dstW/dstH are always even (NV12 4:2:0 requires it, and CaptureSizing.MakeEven guarantees it) — same
        // implicit assumption the original step-by-2 chroma loop already made, just relied upon more directly
        // here since every 2x2 block is now handled as a single unit.
        for (int y = 0; y < dstH; y += 2)
        {
            int sy0 = syFor[y], sy1 = syFor[y + 1];
            int lumaRow0 = y * dstW, lumaRow1 = (y + 1) * dstW;
            for (int x = 0; x < dstW; x += 2)
            {
                int sx0 = sxFor[x], sx1 = sxFor[x + 1];

                int sumU = 0, sumV = 0;
                WritePixel(bgra, srcW, sx0, sy0, output, lumaRow0 + x, ref sumU, ref sumV);
                WritePixel(bgra, srcW, sx1, sy0, output, lumaRow0 + x + 1, ref sumU, ref sumV);
                WritePixel(bgra, srcW, sx0, sy1, output, lumaRow1 + x, ref sumU, ref sumV);
                WritePixel(bgra, srcW, sx1, sy1, output, lumaRow1 + x + 1, ref sumU, ref sumV);

                int at = ySize + (y / 2) * dstW + x;
                output[at] = (byte)Math.Clamp(sumU / 4, 0, 255);
                output[at + 1] = (byte)Math.Clamp(sumV / 4, 0, 255);
            }
        }
    }

    /// <summary>Fetches one source pixel, writes its luma sample, and accumulates its chroma contribution —
    /// the exact same per-sample BT.601 limited-range formulas the original per-pixel implementation used
    /// (coefficients and operation order unchanged), just evaluated once per pixel instead of once for luma
    /// and once again for chroma. <paramref name="sx"/>/<paramref name="sy"/> are already-resolved source
    /// coordinates (from the caller's lookup tables), not destination coordinates needing further scaling.</summary>
    private static void WritePixel(ReadOnlySpan<byte> bgra, int srcW, int sx, int sy, byte[] output, int lumaIndex, ref int sumU, ref int sumV)
    {
        int i = (sy * srcW + sx) * 4;
        byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];

        output[lumaIndex] = (byte)Math.Clamp(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16, 0, 255);
        sumU += ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
        sumV += ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
    }
}
