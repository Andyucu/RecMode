using System.Diagnostics;

namespace RecMode.Capture;

/// <summary>One reporting window's worth of capture-latency statistics, in milliseconds.</summary>
public readonly record struct CaptureLatencyStats(long Frames, double MinMs, double MaxMs, double MeanMs)
{
    public override string ToString() =>
        $"n={Frames} min={MinMs:F1}ms mean={MeanMs:F1}ms max={MaxMs:F1}ms";
}

/// <summary>
/// Measures how long elapses between the compositor <em>rendering</em> a frame and RecMode actually
/// converting it — i.e. the video path's own contribution to A/V offset — using
/// <c>Direct3D11CaptureFrame.SystemRelativeTime</c>, which Microsoft documents as "the QPC time at which the
/// compositor rendered the frame".
/// <para>
/// <b>Why this exists.</b> RecMode's flash+beep harness measures total observed A/V offset (~-85ms on this
/// dev box) but can't attribute it: the flash itself goes through WPF render + DWM composition, and the beep
/// through WASAPI render, so both halves carry latency that isn't part of the recording pipeline at all.
/// This measures one side directly and passively — no beep, no flash, no user-visible artifact, and none of
/// the output-path noise. Whatever it reports is genuinely video-capture latency.
/// </para>
/// <para>
/// <b>Diagnostic only.</b> Nothing reads these numbers to change behaviour. They're logged so a real
/// WGC-capable machine can tell us how much of the measured offset is video-side, which is the input needed
/// to choose between full source-timestamping and a calibrated offset. Deliberately not wired to any
/// automatic correction: correcting against a measurement this project hasn't yet validated on real hardware
/// would be exactly the guess the manual offset setting exists to avoid.
/// </para>
/// <para>
/// <b>Availability.</b> WGC only. DXGI Desktop Duplication has its own equivalent
/// (<c>DXGI_OUTDUPL_FRAME_INFO.LastPresentTime</c>) that isn't wired up here yet, and the GDI fallback has no
/// frame timestamp at all — on those paths this simply reports nothing rather than reporting a wrong number.
/// </para>
/// </summary>
public sealed class CaptureLatencyTracker(TimeSpan reportInterval)
{
    private readonly long _reportIntervalTicks = (long)(reportInterval.TotalSeconds * Stopwatch.Frequency);
    private readonly Lock _sync = new();

    private long _frames;
    private double _minMs = double.MaxValue;
    private double _maxMs = double.MinValue;
    private double _sumMs;
    private long _windowStartTicks;

    /// <summary>
    /// Records one frame's capture latency. <paramref name="frameRenderedAt"/> is the frame's
    /// <c>SystemRelativeTime</c>; <paramref name="now"/> is the current QPC reading in the same domain
    /// (<see cref="Stopwatch.GetTimestamp"/> is <c>QueryPerformanceCounter</c> on Windows, and
    /// <c>SystemRelativeTime</c> is documented as a QPC value, so the two are directly comparable).
    /// <para>
    /// Negative or absurd differences are discarded rather than averaged in: a frame timestamped *after* the
    /// moment we observed it means the two clocks aren't actually the same domain on this system, and
    /// silently folding that into a mean would produce a confident-looking number built on a broken premise.
    /// </para>
    /// </summary>
    public void Record(TimeSpan frameRenderedAt, long now)
    {
        double nowMs = now * 1000.0 / Stopwatch.Frequency;
        double latencyMs = nowMs - frameRenderedAt.TotalMilliseconds;
        if (latencyMs < 0 || latencyMs > 10_000)
        {
            return;
        }

        lock (_sync)
        {
            if (_frames == 0)
            {
                _windowStartTicks = now;
            }
            _frames++;
            _sumMs += latencyMs;
            if (latencyMs < _minMs) _minMs = latencyMs;
            if (latencyMs > _maxMs) _maxMs = latencyMs;
        }
    }

    /// <summary>Returns and resets the current window's stats once <c>reportInterval</c> has elapsed and at
    /// least one frame was recorded; otherwise returns false and leaves the window accumulating.</summary>
    public bool TryTakeReport(long now, out CaptureLatencyStats stats)
    {
        lock (_sync)
        {
            if (_frames == 0 || now - _windowStartTicks < _reportIntervalTicks)
            {
                stats = default;
                return false;
            }

            stats = new CaptureLatencyStats(_frames, _minMs, _maxMs, _sumMs / _frames);
            _frames = 0;
            _sumMs = 0;
            _minMs = double.MaxValue;
            _maxMs = double.MinValue;
            return true;
        }
    }
}
