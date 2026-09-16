namespace RecMode.Capture;

/// <summary>The composited cursor's frame and where it is (plan §7). Extends the webcam frame source's
/// contract deliberately: the existing GPU upload/composite path then accepts it unchanged, and the only
/// addition is the position, which the pipeline needs to place the stream's destination rect.</summary>
public interface ICursorFrameSource : Webcam.IWebcamFrameSource
{
    /// <summary>The smoothed cursor position in <em>source/texture</em> pixels (top-left of the cursor image,
    /// already hotspot-adjusted), or false while the cursor is hidden or off-screen. Source pixels — not
    /// output — so the pipeline can map it through the crop/zoom exactly like the redaction rect.</summary>
    bool TryGetPosition(out int x, out int y);
}

/// <summary>
/// Frame-rate-independent exponential smoothing of a moving point — the actual "make the raw captured cursor
/// motion look eased" step. Pure and unit-tested: this is the part of smooth-cursor that decides whether the
/// result reads as polished or as lag, and it is the one part that can be pinned without a GPU.
/// </summary>
public static class CursorSmoothing
{
    /// <summary>Advances <paramref name="current"/> toward <paramref name="target"/> by one
    /// <paramref name="deltaSeconds"/> step. <paramref name="halfLifeSeconds"/> is how long the remaining
    /// distance takes to halve — frame-rate independent by construction, so the motion looks identical at 30
    /// and 60 fps (a fixed per-frame fraction, the naive version, visibly changes character with the frame
    /// rate). A non-positive half-life snaps straight to the target.</summary>
    public static double Advance(double current, double target, double deltaSeconds, double halfLifeSeconds)
    {
        if (halfLifeSeconds <= 0 || deltaSeconds <= 0)
        {
            return target;
        }

        double factor = 1 - Math.Pow(0.5, deltaSeconds / halfLifeSeconds);
        return current + (target - current) * factor;
    }

    /// <summary>Default half-life for the composited cursor: long enough to erase the raw sampler's steps,
    /// short enough that the pointer never feels detached from the user's hand.</summary>
    public const double DefaultHalfLifeSeconds = 0.045;
}
