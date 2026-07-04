namespace RecMode.Capture;

/// <summary>What a capture session is pointed at — a whole monitor or a single window (plan Phase 2).</summary>
public sealed record CaptureTarget
{
    public required CaptureKind Kind { get; init; }

    /// <summary>HMONITOR for <see cref="CaptureKind.Monitor"/>, HWND for <see cref="CaptureKind.Window"/>.</summary>
    public required nint Handle { get; init; }

    public required string DisplayName { get; init; }

    public static CaptureTarget FromMonitor(MonitorInfo m) =>
        new() { Kind = CaptureKind.Monitor, Handle = m.Handle, DisplayName = m.DisplayName };

    public static CaptureTarget FromWindow(WindowInfo w) =>
        new() { Kind = CaptureKind.Window, Handle = w.Handle, DisplayName = w.Title };
}

public enum CaptureKind
{
    Monitor,
    Window,
}

/// <summary>A capturable top-level window (Record screen's window picker).</summary>
public sealed record WindowInfo
{
    public required nint Handle { get; init; }
    public required string Title { get; init; }
    public override string ToString() => Title;
}
