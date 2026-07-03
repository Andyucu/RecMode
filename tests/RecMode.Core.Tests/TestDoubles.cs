using RecMode.Core.Errors;

namespace RecMode.Core.Tests;

/// <summary>Collects reported errors so tests can assert on the taxonomy channel.</summary>
internal sealed class RecordingErrorReporter : IErrorReporter
{
    public List<RecModeError> Errors { get; } = [];

    public event EventHandler<RecModeError>? ErrorReported;

    public void Report(RecModeError error)
    {
        Errors.Add(error);
        ErrorReported?.Invoke(this, error);
    }
}

/// <summary>A disposable temp directory that cleans itself up.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "recmode-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
