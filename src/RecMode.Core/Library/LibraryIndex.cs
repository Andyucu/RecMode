using System.Text.Json;
using System.Text.Json.Serialization;
using RecMode.Core.Infrastructure;
using Serilog;

namespace RecMode.Core.Library;

/// <summary>
/// Capture metadata for one finished recording, stored in the library index (plan §5 — "capture-source
/// metadata in the library index from day one"). Keyed by file name; primitives only so it round-trips
/// cleanly and doesn't couple to the encoder/settings enums.
/// </summary>
public sealed record LibraryIndexEntry(
    string FileName,
    string Source,
    string Codec,
    string Container,
    int Width,
    int Height,
    int Fps,
    double DurationSeconds,
    DateTimeOffset CreatedAt,
    int Quality = 0,
    bool SystemAudioEnabled = false,
    bool MicrophoneEnabled = false);

/// <summary>Reads/writes the recordings metadata index (<c>library.json</c>).</summary>
public interface ILibraryIndex
{
    /// <summary>Adds (or replaces by file name) an entry and persists.</summary>
    void Add(LibraryIndexEntry entry);

    /// <summary>All entries, keyed by file name. Empty if the index is missing or unreadable.</summary>
    IReadOnlyDictionary<string, LibraryIndexEntry> ByFileName();
}

/// <summary>Default <see cref="ILibraryIndex"/> — a JSON array at <see cref="IAppPaths.LibraryIndexPath"/> (portable-safe).</summary>
public sealed class LibraryIndex(IAppPaths paths) : ILibraryIndex
{
    // Bounds the index file so it can't grow unboundedly; the Library also falls back to the filesystem for
    // listing, so files beyond this cap are still visible — they just lose saved metadata (resolution, codec,
    // "record again" source, etc.) and are evicted oldest-CreatedAt-first. Not currently surfaced in the UI;
    // see README/roadmap for tracking a scalable replacement (e.g. SQLite) if this cap becomes a real problem.
    public const int MaxEntries = 1000;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Add(LibraryIndexEntry entry)
    {
        lock (_lock)
        {
            List<LibraryIndexEntry> entries = Load();
            entries.RemoveAll(e => string.Equals(e.FileName, entry.FileName, StringComparison.OrdinalIgnoreCase));
            entries.Add(entry);

            if (entries.Count > MaxEntries)
            {
                entries = entries.OrderByDescending(e => e.CreatedAt).Take(MaxEntries).ToList();
            }

            Write(entries);
        }
    }

    public IReadOnlyDictionary<string, LibraryIndexEntry> ByFileName()
    {
        lock (_lock)
        {
            var map = new Dictionary<string, LibraryIndexEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (LibraryIndexEntry e in Load())
            {
                map[e.FileName] = e;
            }

            return map;
        }
    }

    private List<LibraryIndexEntry> Load()
    {
        try
        {
            if (!File.Exists(paths.LibraryIndexPath))
            {
                return [];
            }

            string json = File.ReadAllText(paths.LibraryIndexPath);
            return JsonSerializer.Deserialize<List<LibraryIndexEntry>>(json, Options) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt/locked index is non-fatal — the Library still lists files from disk — but this is
            // otherwise invisible, so log it: repeated occurrences point at permissions or corruption that
            // won't surface any other way (recordings keep succeeding while metadata quietly stops persisting).
            Log.Warning(ex, "Could not read the library index at {Path}; recordings will still be listed from disk but without saved metadata", paths.LibraryIndexPath);
            return [];
        }
    }

    private void Write(List<LibraryIndexEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.LibraryIndexPath)!);
            string json = JsonSerializer.Serialize(entries, Options);
            string temp = paths.LibraryIndexPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, paths.LibraryIndexPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a failed index write must never fail the recording. Still worth logging —
            // a persistently failing write silently strips metadata from every future recording.
            Log.Warning(ex, "Could not write the library index at {Path}", paths.LibraryIndexPath);
        }
    }
}
