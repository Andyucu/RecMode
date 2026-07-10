namespace RecMode.Core.Settings;

/// <summary>
/// A named bundle of Record-screen settings (plan §7 backlog #4 — "Profiles/presets", pulled forward into
/// 1.0 in a scoped-down form: switches container/frame rate/quality/audio, not the encoder itself, since hw
/// encoder availability is machine-specific). "OBS-style scenes, but simpler."
/// </summary>
public sealed class RecordingProfile
{
    public string Name { get; set; } = "Custom profile";
    public MediaContainer Container { get; set; } = MediaContainer.Mp4;
    public int FrameRate { get; set; } = 60;

    /// <summary>0–100 quality slider (plan §3.3 model: CRF = 51 − q·0.38).</summary>
    public int Quality { get; set; } = 70;

    public bool SystemAudioEnabled { get; set; } = true;
    public bool MicrophoneEnabled { get; set; }
    public AudioCodec AudioCodec { get; set; } = AudioCodec.Aac;
    public int AudioBitrateKbps { get; set; } = 192;

    /// <summary>True for the shipped presets (Tutorial/Gameplay/…) — can't be deleted, only copied into a new custom profile.</summary>
    public bool IsBuiltIn { get; set; }

    public override string ToString() => Name;
}

/// <summary>
/// The shipped presets named in the plan's backlog item — Tutorial / Gameplay / Meeting / Bug report / GIF
/// clip / High-quality archive. "GIF clip" approximates a short, low-fidelity shareable clip (true animated
/// GIF/WebP export is a separate, still-backlog item — plan §7 #3); "High-quality archive" approximates
/// near-lossless with the existing quality slider maxed + lossless FLAC audio (a true lossless x264/FFV1
/// video profile is also still-backlog, plan §7 #4).
/// </summary>
public static class RecordingProfiles
{
    public static IReadOnlyList<RecordingProfile> BuiltIn { get; } =
    [
        new()
        {
            Name = "Tutorial (Balanced quality, 30 fps)", IsBuiltIn = true, Container = MediaContainer.Mp4, FrameRate = 30, Quality = 75,
            SystemAudioEnabled = true, MicrophoneEnabled = true, AudioCodec = AudioCodec.Aac, AudioBitrateKbps = 192,
        },
        new()
        {
            Name = "Gameplay (High quality, 60 fps)", IsBuiltIn = true, Container = MediaContainer.Mp4, FrameRate = 60, Quality = 85,
            SystemAudioEnabled = true, MicrophoneEnabled = false, AudioCodec = AudioCodec.Aac, AudioBitrateKbps = 192,
        },
        new()
        {
            Name = "Meeting (Standard quality, 30 fps)", IsBuiltIn = true, Container = MediaContainer.Mp4, FrameRate = 30, Quality = 60,
            SystemAudioEnabled = true, MicrophoneEnabled = true, AudioCodec = AudioCodec.Aac, AudioBitrateKbps = 128,
        },
        new()
        {
            Name = "Bug report (Small file, 30 fps)", IsBuiltIn = true, Container = MediaContainer.Mp4, FrameRate = 30, Quality = 55,
            SystemAudioEnabled = true, MicrophoneEnabled = false, AudioCodec = AudioCodec.Aac, AudioBitrateKbps = 128,
        },
        new()
        {
            Name = "Quick clip (Low quality, 15 fps, no audio)", IsBuiltIn = true, Container = MediaContainer.Mp4, FrameRate = 15, Quality = 50,
            SystemAudioEnabled = false, MicrophoneEnabled = false, AudioCodec = AudioCodec.Aac, AudioBitrateKbps = 128,
        },
        new()
        {
            Name = "Archive (Maximum quality, 60 fps, lossless audio)", IsBuiltIn = true, Container = MediaContainer.Mkv, FrameRate = 60, Quality = 95,
            SystemAudioEnabled = true, MicrophoneEnabled = true, AudioCodec = AudioCodec.Flac, AudioBitrateKbps = 192,
        },
    ];
}
