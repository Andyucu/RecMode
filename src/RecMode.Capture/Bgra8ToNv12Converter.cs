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
    public static void Convert(byte[] bgra, int srcW, int srcH, int dstW, int dstH, byte[] output)
    {
        int ySize = dstW * dstH;
        for (int y = 0; y < dstH; y++)
        for (int x = 0; x < dstW; x++)
        {
            (byte b, byte g, byte r) = Pixel(bgra, srcW, srcH, dstW, dstH, x, y);
            output[y * dstW + x] = (byte)Math.Clamp((77 * r + 150 * g + 29 * b + 128) >> 8, 0, 255);
        }
        for (int y = 0; y < dstH; y += 2)
        for (int x = 0; x < dstW; x += 2)
        {
            int sumU = 0, sumV = 0;
            for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
            {
                (byte b, byte g, byte r) = Pixel(bgra, srcW, srcH, dstW, dstH, x + dx, y + dy);
                sumU += (-43 * r - 85 * g + 128 * b + 32768) >> 8;
                sumV += (128 * r - 107 * g - 21 * b + 32768) >> 8;
            }
            int at = ySize + (y / 2) * dstW + x;
            output[at] = (byte)Math.Clamp(sumU / 4, 0, 255);
            output[at + 1] = (byte)Math.Clamp(sumV / 4, 0, 255);
        }
    }

    private static (byte B, byte G, byte R) Pixel(byte[] p, int w, int h, int dstW, int dstH, int x, int y)
    {
        int sx = Math.Min(w - 1, x * w / dstW), sy = Math.Min(h - 1, y * h / dstH);
        int i = (sy * w + sx) * 4;
        return (p[i], p[i + 1], p[i + 2]);
    }
}
