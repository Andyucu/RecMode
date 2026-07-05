using System.Text.Json;
using System.Text.Json.Serialization;
using RecMode.Core.Infrastructure;

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
    DateTimeOffset CreatedAt);

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
    private const int MaxEntries = 1000; // bound the file; the Library also falls back to the filesystem
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
            return []; // corrupt/locked index is non-fatal — the Library still lists files from disk
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
            // Best-effort: a failed index write must never fail the recording.
        }
    }
}
