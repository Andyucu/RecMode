using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;

namespace RecMode.App.Services;

/// <summary>
/// Builds encoder fallback chains for <see cref="RecordingCoordinator"/>'s encoder-selection logic (§3.6):
/// selected → same-codec other backend → software libx264 baseline → any hardware H.264; or an
/// all-software chain for the same codec (used by the mid-stream hw→sw Degraded downgrade). Pure — depends
/// only on <see cref="IEncoderProbe"/>, no coordinator state — so it's independently unit-testable with a
/// fake probe rather than only verifiable by driving a real recording.
/// </summary>
internal sealed class EncoderFallbackChain(IEncoderProbe encoderProbe)
{
    /// <summary>Selected encoder first, then same-codec alternates, then libx264 CPU baseline, then any hardware H.264.</summary>
    public List<EncoderInfo> Build(EncoderInfo selected)
    {
        IReadOnlyList<EncoderInfo> available = encoderProbe.GetAvailableEncoders();
        var chain = new List<EncoderInfo> { selected };

        void Add(EncoderInfo? e)
        {
            if (e is not null && !chain.Exists(c => c.FfmpegId == e.FfmpegId))
            {
                chain.Add(e);
            }
        }

        foreach (EncoderInfo e in available.Where(x => x.Codec == selected.Codec))
        {
            Add(e); // same codec, other backends
        }
        Add(available.FirstOrDefault(x => x.FfmpegId == "libx264")); // baseline VM/no-GPU CPU path
        foreach (EncoderInfo e in available.Where(x => x is { Codec: VideoCodec.H264, IsHardware: true }))
        {
            Add(e); // any hardware H.264
        }

        return chain;
    }

    /// <summary>Software-only fallback chain for the same codec as <paramref name="current"/> (last resort:
    /// libx264), for the hw→sw downgrade — never returns a hardware encoder.</summary>
    public List<EncoderInfo> BuildSoftwareOnly(EncoderInfo current)
    {
        IReadOnlyList<EncoderInfo> available = encoderProbe.GetAvailableEncoders();
        var chain = new List<EncoderInfo>();

        void Add(EncoderInfo? e)
        {
            if (e is not null && !chain.Exists(c => c.FfmpegId == e.FfmpegId))
            {
                chain.Add(e);
            }
        }

        foreach (EncoderInfo e in available.Where(x => x.Codec == current.Codec && !x.IsHardware))
        {
            Add(e);
        }
        Add(available.FirstOrDefault(x => x.FfmpegId == "libx264")); // last-resort software

        return chain;
    }
}
