namespace RecMode.Core.Infrastructure;

/// <summary>
/// Shared temp-file-then-rename write, used by every JSON store in the app (settings, library index,
/// encoder cache) so a process kill mid-write never leaves a torn/truncated file. The rename itself
/// (<see cref="File.Move(string, string, bool)"/>/<see cref="File.Replace(string, string, string?)"/>) is
/// atomic at the filesystem-metadata level on NTFS, but that alone isn't enough: without an explicit flush to
/// physical disk before the rename, the temp file's actual data can still be sitting only in the OS page
/// cache — a power loss between the write and the rename (or even right after, before the cache lazily
/// writes back) can leave the renamed file present in the directory but with stale, zero, or garbage content,
/// silently defeating the whole point of the atomic-rename pattern.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>Writes <paramref name="contents"/> to <paramref name="path"/> via a same-directory temp file,
    /// flushed to physical disk, then atomically renamed into place.</summary>
    public static void Write(string path, string contents)
    {
        string tempPath = path + ".tmp";
        using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (StreamWriter writer = new(stream))
        {
            writer.Write(contents);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
