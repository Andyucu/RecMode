using System.IO;
using System.Text;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>One chapter to write into a recording, in file-local seconds (segment-local for auto-split).</summary>
public sealed record ChapterMark(double StartSeconds, double EndSeconds, string Title);

/// <summary>
/// Builds the ffmetadata document ffmpeg reads to write real, seekable chapters into MP4/MKV with
/// <c>-c copy</c> (no re-encode). Verified against the bundled ffmpeg for both containers.
/// <para>
/// <b>The file must start exactly with <c>;FFMETADATA1</c>.</b> A UTF-8 BOM makes ffmpeg reject the document
/// outright ("Invalid data found when processing input"), and it is easy to introduce by accident — PowerShell's
/// <c>-Encoding utf8</c> emits one, and so do several editors' "UTF-8 with signature" defaults. Hence the
/// explicit BOM-less <see cref="UTF8Encoding"/> here, plus a test pinning the first bytes.
/// </para>
/// </summary>
public static class ChapterMetadata
{
    public const string Header = ";FFMETADATA1";

    /// <summary>Millisecond timebase, so chapter boundaries survive the round-trip exactly (a 1/1000 timebase
    /// keeps whole-millisecond precision; the default 1/1000000000 form is what ffmpeg writes back out).</summary>
    private const string Timebase = "1/1000";

    /// <summary>The ffmetadata document for <paramref name="chapters"/>. Pure, so the format is unit-testable
    /// without ffmpeg.</summary>
    public static string Build(IReadOnlyList<ChapterMark> chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);

        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        foreach (ChapterMark chapter in chapters)
        {
            sb.Append("[CHAPTER]\n");
            sb.Append("TIMEBASE=").Append(Timebase).Append('\n');
            sb.Append("START=").Append(Milliseconds(chapter.StartSeconds)).Append('\n');
            sb.Append("END=").Append(Milliseconds(chapter.EndSeconds)).Append('\n');
            sb.Append("title=").Append(Escape(chapter.Title)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Writes <see cref="Build"/> to a temporary file next to the recording and returns its path.
    /// Caller deletes it (see <see cref="Remuxer"/>), which does so in a finally.</summary>
    public static string WriteTempFile(IReadOnlyList<ChapterMark> chapters, string directory)
    {
        string path = Path.Combine(directory, $".recmode-chapters-{Guid.NewGuid():N}.txt");
        // BOM-less, explicitly — see the class doc comment.
        File.WriteAllText(path, Build(chapters), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static long Milliseconds(double seconds) =>
        (long)Math.Round(Math.Max(0, seconds) * 1000, MidpointRounding.AwayFromZero);

    /// <summary>ffmetadata treats <c>\</c>, <c>=</c>, <c>;</c> and <c>#</c> as special and a raw newline ends the
    /// value; titles here are generated (or short user text), but escaping keeps a stray character from
    /// truncating the document and silently dropping later chapters.</summary>
    private static string Escape(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return "";
        }

        var sb = new StringBuilder(title.Length + 8);
        foreach (char c in title)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '=': sb.Append("\\="); break;
                case ';': sb.Append("\\;"); break;
                case '#': sb.Append("\\#"); break;
                case '\n' or '\r': sb.Append(' '); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }
}
