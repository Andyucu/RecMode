using System.Globalization;

namespace RecMode.Core.Recording;

/// <summary>
/// Expands the user's filename pattern (plan §3.8) — <c>{date} {time} {source} {codec}</c> — sanitizes
/// illegal characters, and resolves collisions with a numeric suffix. Pure/testable.
/// </summary>
public static class FilenameBuilder
{
    public static string BuildFileName(string pattern, DateTimeOffset when, string source, string codec, string extension)
    {
        string expanded = (string.IsNullOrWhiteSpace(pattern) ? "RecMode {date} {time}" : pattern)
            .Replace("{date}", when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{time}", when.ToString("HH-mm-ss", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{source}", source, StringComparison.Ordinal)
            .Replace("{codec}", codec, StringComparison.Ordinal);

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

    private static string Sanitize(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        int n = 0;
        foreach (char c in name)
        {
            buffer[n++] = Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c;
        }

        string result = new string(buffer[..n]).Trim();
        return result.Length == 0 ? "RecMode" : result;
    }
}
