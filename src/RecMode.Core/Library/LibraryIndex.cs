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

    /// <summary>Removes the metadata when its recording is deleted.</summary>
    void Remove(string fileName);

    /// <summary>Points an existing entry at its file's new name after an on-disk rename, preserving its
    /// metadata. No-op if <paramref name="oldFileName"/> isn't indexed (e.g. a plain filesystem-only entry).</summary>
    void Rename(string oldFileName, string newFileName);

    /// <summary>Prunes entries for recordings no longer present in the active recording folder.</summary>
    void PruneMissing(ISet<string> fileNames);
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
                // Evict from the *other* entries only. Sorting the whole list by CreatedAt and taking the
                // newest N could discard the entry being added right now, if its timestamp happened to be
                // older than a thousand existing ones — reachable with clock skew or a restored library.json,
                // and the symptom ("my newest recording has no metadata and no Record-again button") would
                // never be attributed to eviction.
                entries = entries.Where(e => !ReferenceEquals(e, entry))
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(MaxEntries - 1)
                    .Append(entry)
                    .ToList();
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

    public void Remove(string fileName)
    {
        lock (_lock)
        {
            List<LibraryIndexEntry> entries = Load();
            if (entries.RemoveAll(e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                Write(entries);
            }
        }
    }

    public void Rename(string oldFileName, string newFileName)
    {
        lock (_lock)
        {
            List<LibraryIndexEntry> entries = Load();
            int i = entries.FindIndex(e => string.Equals(e.FileName, oldFileName, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                return;
            }

            // Renaming onto an already-indexed name (a genuine collision, not the normal case) drops the
            // stale duplicate the same way Add() already does for its own re-index case, rather than leaving
            // two entries for one file name.
            entries.RemoveAll(e => string.Equals(e.FileName, newFileName, StringComparison.OrdinalIgnoreCase));
            i = entries.FindIndex(e => string.Equals(e.FileName, oldFileName, StringComparison.OrdinalIgnoreCase));
            entries[i] = entries[i] with { FileName = newFileName };
            Write(entries);
        }
    }

    public void PruneMissing(ISet<string> fileNames)
    {
        lock (_lock)
        {
            List<LibraryIndexEntry> entries = Load();
            if (entries.RemoveAll(e => !fileNames.Contains(e.FileName)) > 0)
            {
                Write(entries);
            }
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
            List<LibraryIndexEntry> entries = JsonSerializer.Deserialize<List<LibraryIndexEntry>>(json, Options) ?? [];

            // LibraryIndexEntry is a positional record, so System.Text.Json builds it through the primary
            // constructor and fills any *missing* member with default — a JSON object with no "FileName"
            // deserializes cleanly to an entry whose FileName is null, and a bare `null` array element
            // deserializes to a null entry. Both are structurally valid JSON, so the JsonException catch
            // below never fires for them; they'd instead blow up later in ByFileName (null dictionary key),
            // PruneMissing (null into an OrdinalIgnoreCase HashSet), or Add (NRE) — and Add runs inside
            // Finalize(), where until now an exception permanently wedged the recorder. Drop them here,
            // where malformed-index tolerance already lives, so the rest of this class can assume non-null.
            int dropped = entries.RemoveAll(e => e is null || string.IsNullOrEmpty(e.FileName));
            if (dropped > 0)
            {
                Log.Warning("Dropped {Count} malformed entr{Suffix} from the library index at {Path}",
                    dropped, dropped == 1 ? "y" : "ies", paths.LibraryIndexPath);
            }

            return entries;
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
            AtomicFileWriter.Write(paths.LibraryIndexPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a failed index write must never fail the recording. Still worth logging —
            // a persistently failing write silently strips metadata from every future recording.
            Log.Warning(ex, "Could not write the library index at {Path}", paths.LibraryIndexPath);
        }
    }
}
