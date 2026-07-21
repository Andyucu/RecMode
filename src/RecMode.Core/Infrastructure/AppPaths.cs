namespace RecMode.Core.Infrastructure;

/// <summary>
/// Default <see cref="IAppPaths"/>. Portable mode is selected by a <c>portable.marker</c> file next to
/// the executable (plan §3.5). All directory composition happens here and nowhere else.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public const string PortableMarkerFileName = "portable.marker";
    public const string SettingsFileName = "settings.json";
    public const string LibraryIndexFileName = "library.json";

    private const string AppFolderName = "RecMode";

    public AppPaths(string? appDirectory = null)
    {
        AppDirectory = appDirectory ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        IsPortable = File.Exists(Path.Combine(AppDirectory, PortableMarkerFileName));

        if (IsPortable)
        {
            DataDirectory = Path.Combine(AppDirectory, "Data");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            RecordingsDirectory = Path.Combine(AppDirectory, "Recordings");
            FfmpegDirectory = Path.Combine(AppDirectory, "ffmpeg");
            LicensesDirectory = Path.Combine(AppDirectory, "licenses");
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            DataDirectory = Path.Combine(appData, AppFolderName);
            LogsDirectory = Path.Combine(localAppData, AppFolderName, "logs");
            RecordingsDirectory = Path.Combine(videos, AppFolderName);
            // Installed builds still ship ffmpeg alongside the exe; licenses too.
            FfmpegDirectory = Path.Combine(AppDirectory, "ffmpeg");
            LicensesDirectory = Path.Combine(AppDirectory, "licenses");
        }

        CrashDumpDirectory = Path.Combine(LogsDirectory, "crash");
        ScreenshotsDirectory = Path.Combine(RecordingsDirectory, "Screenshots");
        SettingsFilePath = Path.Combine(DataDirectory, SettingsFileName);
        LibraryIndexPath = Path.Combine(DataDirectory, LibraryIndexFileName);
    }

    public bool IsPortable { get; }
    public string AppDirectory { get; }
    public string DataDirectory { get; }
    public string LogsDirectory { get; }
    public string CrashDumpDirectory { get; }
    public string RecordingsDirectory { get; }
    public string ScreenshotsDirectory { get; }
    public string FfmpegDirectory { get; }
    public string LicensesDirectory { get; }
    public string SettingsFilePath { get; }
    public string LibraryIndexPath { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(CrashDumpDirectory);
        Directory.CreateDirectory(RecordingsDirectory);
        Directory.CreateDirectory(ScreenshotsDirectory);
    }

    public bool IsDataDirectoryWritable()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string probe = Path.Combine(DataDirectory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
