using System.Globalization;

namespace RecMode.Core.Recording;

/// <summary>
/// Expands the user's filename pattern (plan §3.8) — <c>{date} {time} {source} {codec}</c> — sanitizes
/// illegal characters, and resolves collisions with a numeric suffix. Pure/testable.
/// </summary>
public static class FilenameBuilder
{
    // Windows paths top out around 260 chars total, so no legitimate expanded name is anywhere near this —
    // it exists purely to bound the settings-driven filename pattern (free-text, unbounded in the Settings
    // UI) before Sanitize ever sees it.
    private const int MaxNameLength = 200;

    public static string BuildFileName(string pattern, DateTimeOffset when, string source, string codec, string extension)
    {
        string expanded = (string.IsNullOrWhiteSpace(pattern) ? "RecMode {date} {time}" : pattern)
            .Replace("{date}", when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{time}", when.ToString("HH-mm-ss", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{source}", source, StringComparison.Ordinal)
            .Replace("{codec}", codec, StringComparison.Ordinal);
        if (expanded.Length > MaxNameLength)
        {
            expanded = expanded[..MaxNameLength];
        }

        string safe = Sanitize(expanded);
        return $"{safe}.{extension.TrimStart('.')}";
    }

    /// <summary>
    /// Auto-split (plan §3.3): segment 1 keeps <paramref name="baseFileName"/> as-is; later segments get a
    /// "_partN" suffix before the extension (e.g. "Recording.mp4" → "Recording_part2.mp4").
    /// </summary>
    public static string SegmentFileName(string baseFileName, int index)
    {
        if (index <= 1)
        {
            return baseFileName;
        }

        string stem = Path.GetFileNameWithoutExtension(baseFileName);
        string ext = Path.GetExtension(baseFileName);
        return $"{stem}_part{index}{ext}";
    }

    /// <summary>Full path under <paramref name="directory"/>, suffixed if the file already exists.</summary>
    public static string BuildUniquePath(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int i = 2; i < 10000; i++)
        {
            string candidate = Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return path;
    }

    // Legacy DOS device names: Win32's CreateFile (which ffmpeg — a plain Win32 process — calls without the
    // \\?\ prefix .NET itself uses internally) resolves these to the device regardless of what follows, so
    // both "NUL.mp4" AND "NUL.2026-07-28.mkv" open the NUL device, not a file named that — Win32 matches a
    // reserved name against the path component up to its *first* dot, ignoring every dot after (confirmed
    // against documented Win32 path-parsing behavior). .NET's own File.Exists/File.WriteAllText don't hit
    // this (they use the \\?\-prefixed path APIs), so a pattern that expands to one of these — including via
    // a pattern like "NUL.{date}", not just the bare device name — produces a "successful" recording that
    // silently doesn't exist on disk. Matched against the stem only (before the extension BuildFileName
    // appends), and only the portion of that stem up to its own first dot.
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static string Sanitize(string name)
    {
        // Heap-allocated rather than stackalloc'd: name's length traces back to the free-text filename
        // pattern in Settings, which BuildFileName bounds but this method has no way to re-verify on its
        // own — an unbounded stackalloc here would be an uncatchable StackOverflowException waiting to
        // happen the moment that guarantee is ever violated by a future caller.
        char[] buffer = new char[name.Length];
        int n = 0;
        foreach (char c in name)
        {
            buffer[n++] = Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c;
        }

        string result = new string(buffer, 0, n).Trim();
        if (result.Length == 0)
        {
            return "RecMode";
        }

        int firstDot = result.IndexOf('.');
        string leadingComponent = firstDot < 0 ? result : result[..firstDot];
        return ReservedDeviceNames.Contains(leadingComponent) ? $"_{result}" : result;
    }
}
