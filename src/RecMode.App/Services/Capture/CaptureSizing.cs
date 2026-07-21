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

        bool hardwareH264 = encoder.Codec == VideoCodec.H264 && encoder.IsHardware;
        if (hardwareH264 && w > HardwareH264MaxWidth)
        {
            double scale = HardwareH264MaxWidth / (double)w;
            w = HardwareH264MaxWidth;
            h = (int)Math.Round(srcH * scale);
        }

        return (MakeEven(w), MakeEven(h));
    }

    private static int MakeEven(int v) => v % 2 == 0 ? v : v - 1;
}
