using System.Globalization;

namespace RecMode.Core.Settings;

/// <summary>
/// Pure "should this schedule fire now?" logic for the scheduler engine (plan Phase 8). A schedule fires once
/// within the minute matching its start time, on days matching its recurrence, de-duplicated via
/// <see cref="ScheduleItem.LastFiredUtc"/>. No timers/IO here so it's unit-testable.
/// </summary>
public static class ScheduleEvaluator
{
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan WeeklyPeriod = TimeSpan.FromDays(7) - TimeSpan.FromHours(1); // slack

    /// <summary>True if <paramref name="item"/> should start a recording at <paramref name="now"/> (local time).</summary>
    public static bool IsDue(ScheduleItem item, DateTimeOffset now)
    {
        if (!item.Enabled)
        {
            return false;
        }

        if (!TryParseTime(item.Time, out TimeOnly target))
        {
            return false;
        }

        // Only within the target minute.
        if (now.Hour != target.Hour || now.Minute != target.Minute)
        {
            return false;
        }

        string occurrence = OccurrenceKey(now, target);
        if (string.Equals(item.LastFiredOccurrence, occurrence, StringComparison.Ordinal))
        {
            return false;
        }

        // Backward compatibility for schedules saved before occurrence keys were introduced.
        if (item.LastFiredUtc is { } last && now - last < DedupWindow)
        {
            return false;
        }

        return item.Recurrence switch
        {
            ScheduleRecurrence.Once => IsOnceDue(item, now, target),
            ScheduleRecurrence.Daily => true,
            ScheduleRecurrence.Weekdays => now.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday,
            // WeeklyDay was added after schedules had already shipped. Keep old JSON working by retaining
            // the old seven-day cadence when it is absent; newly saved schedules always have a real weekday.
            ScheduleRecurrence.Weekly => item.WeeklyDay is { } weeklyDay
                ? now.DayOfWeek == weeklyDay
                : item.LastFiredUtc is not { } l || now - l >= WeeklyPeriod,
            _ => false,
        };
    }

    /// <summary>The single parse for a <see cref="ScheduleItem.Time"/> string — exact "HH:mm", invariant
    /// culture, trimmed. Exposed (rather than kept inline in <see cref="IsDue"/>) because the scheduler must
    /// parse the same string a second time when writing <see cref="ScheduleItem.LastFiredOccurrence"/>, and
    /// it previously used a culture-sensitive <c>TimeOnly.TryParse</c> there instead. On any culture where
    /// that parse fails, the discarded result left <c>default</c> (00:00), so the occurrence key was written
    /// as "…|00:00" and could never match the "…|HH:mm" key <see cref="IsDue"/> computes — silently disabling
    /// the occurrence dedup that exists specifically for the repeated hour at DST end, leaving only the 90 s
    /// <see cref="DedupWindow"/> fallback.</summary>
    public static bool TryParseTime(string? time, out TimeOnly value) =>
        TimeOnly.TryParseExact(time?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    public static string OccurrenceKey(DateTimeOffset now, TimeOnly target) =>
        $"{now:yyyy-MM-dd}|{target:HH\\:mm}";

    private static bool IsOnceDue(ScheduleItem item, DateTimeOffset now, TimeOnly target)
    {
        if (item.LastFiredUtc is not null) return false;
        // Existing files predate OnceAt; preserve their former one-shot behavior. Newly-created/edited
        // schedules always have a date and therefore cannot run on a later day if today's minute was missed.
        if (item.OnceAt is not { } once) return true;
        return once.Date == now.Date && once.Hour == target.Hour && once.Minute == target.Minute;
    }
}
