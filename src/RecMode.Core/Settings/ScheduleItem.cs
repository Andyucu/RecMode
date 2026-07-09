namespace RecMode.Core.Settings;

/// <summary>How often a scheduled recording repeats.</summary>
public enum ScheduleRecurrence
{
    Once,
    Daily,
    Weekdays,
    Weekly,
}

/// <summary>
/// A persisted scheduled-recording definition (plan Phase 6 UI + data model; the firing engine lands in
/// Phase 8). Kept as a plain mutable POCO so it round-trips through the settings JSON. Source/encoder follow
/// the current Record settings for the MVP, so only the trigger + duration are stored here.
/// </summary>
public sealed class ScheduleItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New schedule";
    public ScheduleRecurrence Recurrence { get; set; } = ScheduleRecurrence.Once;

    /// <summary>Start time of day, "HH:mm" (24-hour).</summary>
    public string Time { get; set; } = "18:00";

    public int DurationMinutes { get; set; } = 30;
    public bool Enabled { get; set; } = true;

    /// <summary>Name of the <see cref="RecordingProfile"/> to apply when this schedule fires; <c>null</c> (the
    /// default, and the original MVP behavior) means "use whatever the Record screen is currently set to."</summary>
    public string? ProfileName { get; set; }

    /// <summary>When this schedule last fired (dedup + Once/Weekly tracking). Null = never fired.</summary>
    public DateTimeOffset? LastFiredUtc { get; set; }
}
