namespace RecMode.App.Services;

/// <summary>
/// Cheap black-frame heuristic for <see cref="RecordingCoordinator"/>'s black-frame watchdog (§3.6):
/// exclusive-fullscreen games and DRM-protected windows capture as black. Pure — no coordinator state — so
/// it's independently unit-testable rather than only verifiable by driving a real recording.
/// </summary>
internal static class BlackFrameDetector
{
    private const int SampleCount = 256;
    private const byte StudioBlackThreshold = 20; // studio-black luma is ~16

    /// <summary>Samples the NV12 luma plane; near-zero everywhere ≈ black.</summary>
    public static bool IsLikelyBlack(byte[] nv12, int lumaLength)
    {
        if (lumaLength <= 0)
        {
            return false;
        }

        int stepSize = Math.Max(1, lumaLength / SampleCount);
        byte max = 0;
        for (int i = 0; i < lumaLength; i += stepSize)
        {
            if (nv12[i] > max)
            {
                max = nv12[i];
            }
        }

        return max < StudioBlackThreshold;
    }
}
