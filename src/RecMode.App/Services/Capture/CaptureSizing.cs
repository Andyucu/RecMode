using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;

namespace RecMode.App.Services;

/// <summary>
/// Resolves the encoded output size from the source monitor, honouring NV12's even-dimension requirement
/// and the hardware H.264 4096-width cap discovered in the Phase 0.5 spike (h264_amf/qsv/nvenc reject
/// wider). Software H.264, HEVC, and AV1 keep native size.
/// </summary>
internal static class CaptureSizing
{
    private const int HardwareH264MaxWidth = 4096;

    public static (int Width, int Height) Resolve(int srcW, int srcH, EncoderInfo encoder)
    {
        int w = srcW, h = srcH;

        // The H.264 level cap is on either dimension, not specifically width — h264_amf rejects a frame
        // whose HEIGHT exceeds 4096 exactly the same way it rejects one whose width does. Checking width
        // only meant a taller-than-wide source (two 4K monitors stacked under All Displays: 3840x4320; a
        // portrait-rotated ultrawide: 1440x5120) passed this check, then failed to open the hardware encoder
        // at all — TryStartAnyEncoder's fallback chain silently walked down to software x264, so the user got
        // a CPU-cost downgrade with only the generic "encoder wouldn't start" warning, never told why.
        bool hardwareH264 = encoder.Codec == VideoCodec.H264 && encoder.IsHardware;
        if (hardwareH264 && (w > HardwareH264MaxWidth || h > HardwareH264MaxWidth))
        {
            double scale = Math.Min(HardwareH264MaxWidth / (double)w, HardwareH264MaxWidth / (double)h);
            w = (int)Math.Round(srcW * scale);
            h = (int)Math.Round(srcH * scale);
        }

        return (MakeEven(w), MakeEven(h));
    }

    private static int MakeEven(int v) => v % 2 == 0 ? v : v - 1;
}
