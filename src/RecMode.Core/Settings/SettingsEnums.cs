namespace RecMode.Core.Settings;

/// <summary>Theme selection. <see cref="System"/> follows the Windows app theme.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>The five accent presets from the design system (plan §2).</summary>
public enum AccentColor
{
    Blue,
    Red,
    Purple,
    Teal,
    Orange,
}

/// <summary>The three shell layouts (sidebar ships first; the others arrive in Phase 6).</summary>
public enum ShellLayout
{
    Sidebar,
    TopTab,
    Compact,
}

public enum VideoCodec
{
    H264,
    Hevc,
    Av1,
}

/// <summary>Preferred encoder backend; <see cref="Auto"/> lets the probe pick the best available.</summary>
public enum EncoderBackend
{
    Auto,
    Nvenc,
    Amf,
    Qsv,
    Software,
}

public enum MediaContainer
{
    Mp4,
    Mkv,
    Mov,
    WebM,
}

public enum AudioCodec
{
    Aac,
    Opus,
    Flac,
}
