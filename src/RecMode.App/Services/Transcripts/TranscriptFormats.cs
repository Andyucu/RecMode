using System.Globalization;
using System.Text;

namespace RecMode.App.Services.Transcripts;

/// <summary>One transcribed utterance with its time range.</summary>
public sealed record TranscriptSegment(double StartSeconds, double EndSeconds, string Text);

/// <summary>A whole recording's transcript, in order.</summary>
public sealed record TranscriptDocument(IReadOnlyList<TranscriptSegment> Segments)
{
    public string PlainText => string.Join(" ", Segments.Select(s => s.Text.Trim()).Where(t => t.Length > 0));
}

/// <summary>
/// Reads and writes the standard caption sidecars (SRT and WebVTT). Pure and unit-tested, because these are
/// the files the user actually keeps: a malformed timestamp makes a caption file useless in every player,
/// and it fails silently (the player just shows nothing or garbage).
/// </summary>
public static class TranscriptFormats
{
    /// <summary>SRT: <c>HH:MM:SS,mmm</c> with a comma before the milliseconds (a dot is the single most
    /// common way to produce an SRT that players reject).</summary>
    public static string ToSrt(IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            TranscriptSegment segment = segments[i];
            sb.Append(i + 1).Append('\n');
            sb.Append(Timestamp(segment.StartSeconds, ',')).Append(" --> ").Append(Timestamp(segment.EndSeconds, ',')).Append('\n');
            sb.Append(segment.Text.Trim()).Append("\n\n");
        }

        return sb.ToString();
    }

    /// <summary>WebVTT: <c>HH:MM:SS.mmm</c> with a dot, and the mandatory <c>WEBVTT</c> header.</summary>
    public static string ToVtt(IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (TranscriptSegment segment in segments)
        {
            sb.Append(Timestamp(segment.StartSeconds, '.')).Append(" --> ").Append(Timestamp(segment.EndSeconds, '.')).Append('\n');
            sb.Append(segment.Text.Trim()).Append("\n\n");
        }

        return sb.ToString();
    }

    /// <summary>Parses the <c>.srt</c> a previous transcription wrote, for the transcript page and its search
    /// box. Deliberately forgiving (tolerates a dot decimal separator and missing indices): the point is to
    /// read back our own files, and refusing to show a transcript over a formatting nit would be worse than
    /// showing it. Returns an empty document for anything unreadable.</summary>
    public static TranscriptDocument ParseSrt(string content)
    {
        var segments = new List<TranscriptSegment>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return new TranscriptDocument(segments);
        }

        string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int index = 0;
        while (index < lines.Length)
        {
            string line = lines[index].Trim();
            int arrow = line.IndexOf("-->", StringComparison.Ordinal);
            if (arrow < 0)
            {
                index++;
                continue;
            }

            if (TryParseTimestamp(line[..arrow].Trim(), out double start) &&
                TryParseTimestamp(line[(arrow + 3)..].Trim(), out double end))
            {
                var text = new StringBuilder();
                index++;
                while (index < lines.Length && lines[index].Trim().Length > 0)
                {
                    if (text.Length > 0)
                    {
                        text.Append(' ');
                    }

                    text.Append(lines[index].Trim());
                    index++;
                }

                segments.Add(new TranscriptSegment(start, end, text.ToString()));
                continue;
            }

            index++;
        }

        return new TranscriptDocument(segments);
    }

    private static bool TryParseTimestamp(string text, out double seconds)
    {
        seconds = 0;
        string[] parts = text.Replace(',', '.').Split(':');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double secs))
        {
            return false;
        }

        seconds = hours * 3600 + minutes * 60 + secs;
        return true;
    }

    private static string Timestamp(double seconds, char separator)
    {
        if (seconds < 0 || double.IsNaN(seconds))
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}{separator}{time.Milliseconds:000}");
    }
}
