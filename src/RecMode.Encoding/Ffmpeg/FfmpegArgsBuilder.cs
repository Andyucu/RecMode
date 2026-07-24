using System.Globalization;
using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>Everything needed to build one recording's ffmpeg command line.</summary>
public sealed record FfmpegJob
{
    public required EncoderInfo Encoder { get; init; }
    public required MediaContainer Container { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int FrameRate { get; init; }

    /// <summary>0–100 quality slider.</summary>
    public required int Quality { get; init; }

    public required string PipeName { get; init; }
    public required string OutputPath { get; init; }

    /// <summary>Set to add a second (audio) input: f32le 48 kHz stereo over this named pipe.</summary>
    public string? AudioPipeName { get; init; }
    public AudioCodec AudioCodec { get; init; } = AudioCodec.Aac;
    public int AudioBitrateKbps { get; init; } = 192;

    /// <summary>Software-encoder thread cap (§3.3). 0 = ffmpeg default (all cores). Ignored by hardware encoders.</summary>
    public int CpuThreadCap { get; init; }

    /// <summary>Run the ffmpeg process below normal priority so recording doesn't starve foreground work (§3.3).</summary>
    public bool BelowNormalPriority { get; init; }

    /// <summary>Encoder effort tier (§3.3) → per-encoder preset. Balanced = the default preset for each encoder.</summary>
    public EncoderEffort Effort { get; init; } = EncoderEffort.Balanced;

    /// <summary>Adds a <c>-maxrate/-bufsize</c> ceiling alongside CRF/CQ encoding on encoders whose rate-control
    /// mode supports it (see <see cref="FfmpegArgsBuilder.SupportsBitrateGuardrail"/>). Off by default here —
    /// the app-level default lives in <c>RecModeSettings.BitrateGuardrailEnabled</c>.</summary>
    public bool BitrateGuardrailEnabled { get; init; }
}

/// <summary>
/// Builds the ffmpeg argument list for a recording (plan §3.3). Video-only for Phase 1 (audio arrives in
/// Phase 4). Quality maps to per-encoder rate control via <see cref="EffectiveQualityValue"/> — a perceptually
/// curved CRF/CQ/QP model (see its doc comment) with a small per-encoder calibration offset, so the same
/// slider position looks more comparable across software/NVENC/AMF/QSV. Kept deterministic so Phase 3 can
/// snapshot-test it.
/// </summary>
public static class FfmpegArgsBuilder
{
    public static string Build(FfmpegJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Validate(job);

        string videoIn =
            $"-f rawvideo -pix_fmt nv12 -s {job.Width}x{job.Height} -r {job.FrameRate} " +
            $"-i \\\\.\\pipe\\{job.PipeName}";

        string audioIn = "", audioMap = "", audioEnc = "";
        if (job.AudioPipeName is not null)
        {
            audioIn = $"-f f32le -ar 48000 -ac 2 -i \\\\.\\pipe\\{job.AudioPipeName}";
            audioMap = "-map 0:v:0 -map 1:a:0";
            audioEnc = BuildAudioArgs(job.Container, job.AudioCodec, job.AudioBitrateKbps);
        }

        string encoder = BuildEncoderArgs(job);
        string faststart = job.Container == MediaContainer.Mp4 ? "-movflags +faststart" : "";

        // Thread cap only bites on software encoders (hardware offloads to the GPU/ASIC), so don't emit it for hw.
        string threads = job.CpuThreadCap > 0 && !job.Encoder.IsHardware
            ? $"-threads {job.CpuThreadCap} "
            : "";

        return $"-hide_banner -loglevel warning {videoIn} {audioIn} {audioMap} " +
               $"{threads}{encoder} -pix_fmt yuv420p {audioEnc} {faststart} -y \"{job.OutputPath}\"";
    }

    /// <summary>Rejects an <see cref="FfmpegJob"/> that would produce a malformed or nonsensical command line,
    /// so a bad job fails fast at the boundary instead of surfacing as a cryptic ffmpeg process failure.</summary>
    private static void Validate(FfmpegJob job)
    {
        if (string.IsNullOrWhiteSpace(job.Encoder.FfmpegId))
        {
            throw new ArgumentException("Encoder.FfmpegId must be set.", nameof(job));
        }
        if (job.Width <= 0 || job.Width % 2 != 0)
        {
            throw new ArgumentException($"Width must be a positive even number, got {job.Width}.", nameof(job));
        }
        if (job.Height <= 0 || job.Height % 2 != 0)
        {
            throw new ArgumentException($"Height must be a positive even number, got {job.Height}.", nameof(job));
        }
        if (job.FrameRate <= 0)
        {
            throw new ArgumentException($"FrameRate must be positive, got {job.FrameRate}.", nameof(job));
        }
        if (job.CpuThreadCap < 0)
        {
            throw new ArgumentException($"CpuThreadCap can't be negative, got {job.CpuThreadCap}.", nameof(job));
        }
        if (job.AudioPipeName is not null && job.AudioBitrateKbps <= 0)
        {
            throw new ArgumentException($"AudioBitrateKbps must be positive, got {job.AudioBitrateKbps}.", nameof(job));
        }
        if (string.IsNullOrWhiteSpace(job.OutputPath))
        {
            throw new ArgumentException("OutputPath must be set.", nameof(job));
        }
        ValidatePipeName(job.PipeName, nameof(job.PipeName));
        if (job.AudioPipeName is not null)
        {
            ValidatePipeName(job.AudioPipeName, nameof(job.AudioPipeName));
        }
    }

    /// <summary>Pipe names are interpolated directly into a <c>\\.\pipe\{name}</c> path, so they must not
    /// contain path separators or be empty — either would produce a malformed or unintended pipe path.</summary>
    private static void ValidatePipeName(string name, string paramName)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains('/'))
        {
            throw new ArgumentException($"'{name}' is not a valid pipe name.", paramName);
        }
    }

    /// <summary>Audio codec steered by container (plan §3.3): MP4/MOV→AAC, MKV/WebM→Opus, with FLAC where valid.</summary>
    public static string BuildAudioArgs(MediaContainer container, AudioCodec requested, int bitrateKbps)
    {
        AudioCodec codec = container switch
        {
            MediaContainer.WebM => AudioCodec.Opus,                                   // WebM = Opus only
            MediaContainer.Mp4 or MediaContainer.Mov => requested == AudioCodec.Flac  // MP4/MOV can't do Opus
                ? AudioCodec.Aac : requested == AudioCodec.Opus ? AudioCodec.Aac : requested,
            _ => requested, // MKV takes anything
        };

        return codec switch
        {
            AudioCodec.Opus => $"-c:a libopus -b:a {bitrateKbps}k",
            AudioCodec.Flac => "-c:a flac",
            _ => $"-c:a aac -b:a {bitrateKbps}k",
        };
    }

    /// <summary>Standard CRF ceiling (worst quality) for x264/x265/NVENC/AMF/QSV — CRF 0 is technically
    /// available but impractically close to lossless, so the useful floor is 1.</summary>
    public const int MinCrf = 1;
    public const int MaxCrf = 51;

    /// <summary>SVT-AV1 supports CRF up to 63 (vs. the H.264/HEVC-family 0–51 range); clamping AV1 to 51 would
    /// leave the bottom third of its useful low-quality/small-file range unreachable from the slider.</summary>
    public const int MaxCrfAv1 = 63;

    /// <summary>
    /// Gamma applied to the 0–100 slider before mapping it onto the CRF range. CRF's *perceptual* effect is
    /// non-linear — the visual difference between CRF 4→8 is far larger than 40→44 — so a straight linear
    /// slider-to-CRF mapping wastes most of the slider's range on barely-distinguishable low-quality territory.
    /// gamma &gt; 1 compresses the slider's low end (quality 0–50, where each CRF unit matters less) into a
    /// coarser sweep across a wide CRF band, and expands the slider's high end (quality 50–100, where each CRF
    /// unit is visually significant) across a finer sweep — so each notch nearer the top of the slider changes
    /// perceived quality by roughly the same amount as each notch nearer the bottom, instead of the top 40% of
    /// the slider all landing within a couple of CRF units of each other.
    /// </summary>
    private const double QualityCurveGamma = 1.8;

    /// <summary>CRF/CQ/QP correction offsets are approximate, community-documented ballpark figures (this dev
    /// machine only has AMD hardware — NVENC/QSV are unverified here), not measured per-encoder on this
    /// project's own test content. They exist because the same numeric CRF/CQ/QP value doesn't produce
    /// comparable visual quality across encoders: consumer/enthusiast hardware encoders are generally reported
    /// as needing a few points lower (i.e. more bits) than x264's CRF for a similar look at the same nominal
    /// value. Deliberately conservative (small, one direction) rather than a precisely "tuned" number this
    /// project can't actually verify without the hardware. Re-calibrate once NVENC/QSV hardware is available
    /// (folds into the standing vendor-gate re-check item).</summary>
    private static int QualityCorrectionOffset(EncoderBackend backend) => backend switch
    {
        EncoderBackend.Nvenc => -2,
        EncoderBackend.Amf => -2,
        EncoderBackend.Qsv => -1,
        _ => 0, // software (libx264/libx265/libsvtav1) is the calibration reference, offset 0
    };

    /// <summary>Perceptually-curved quality→CRF mapping (see <see cref="QualityCurveGamma"/>), clamped to
    /// <paramref name="maxCrf"/> (51 for H.264/HEVC-family encoders, 63 for SVT-AV1 via <see cref="MaxCrfAv1"/>).
    /// Quality 0 → <paramref name="maxCrf"/> (worst), quality 100 → <see cref="MinCrf"/> (best).</summary>
    public static int QualityToCrf(int quality, int maxCrf = MaxCrf)
    {
        double t = Math.Clamp(quality, 0, 100) / 100.0;
        double curved = Math.Pow(t, QualityCurveGamma);
        double crf = maxCrf - curved * (maxCrf - MinCrf);
        return Math.Clamp((int)Math.Round(crf), MinCrf, maxCrf);
    }

    /// <summary>The actual numeric CRF/CQ/QP/global_quality value that will be passed to ffmpeg for
    /// <paramref name="encoder"/> at <paramref name="quality"/> — the curved <see cref="QualityToCrf"/> mapping
    /// (using AV1's wider range where applicable) plus this encoder's calibration offset, re-clamped to a valid
    /// range. Shared by <see cref="Build"/> and the Record screen's quality label so what the UI shows always
    /// matches what actually gets encoded.</summary>
    public static int EffectiveQualityValue(EncoderInfo encoder, int quality)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        int maxCrf = encoder.Codec == VideoCodec.Av1 ? MaxCrfAv1 : MaxCrf;
        int crf = QualityToCrf(quality, maxCrf);
        return Math.Clamp(crf + QualityCorrectionOffset(encoder.Backend), MinCrf, maxCrf);
    }

    /// <summary>Encoders whose current rate-control mode can take a <c>-maxrate/-bufsize</c> ceiling alongside
    /// CRF/CQ without changing that mode: software CRF and NVENC's existing <c>-rc vbr</c> both document this
    /// combination directly. AMF's <c>-rc cqp</c> (constant QP, deliberately the simplest honest mapping per
    /// the Phase 1 design note below) and QSV's ICQ-style <c>-global_quality</c> mode don't rate-limit under
    /// their current modes — adding the flag there would either no-op or require switching rate-control modes
    /// entirely, which is a bigger, separately-verifiable change, not a "just add a cap" one.</summary>
    public static bool SupportsBitrateGuardrail(EncoderBackend backend) =>
        backend is EncoderBackend.Software or EncoderBackend.Nvenc;

    /// <summary>Bits-per-pixel-per-frame at the low and high end of the quality slider — a rough model for
    /// screen-content H.264/HEVC/AV1 encoding, used both for the optional bitrate guardrail and the Record
    /// screen's estimated-size label. Not a precise predictor (real bitrate depends heavily on scene content),
    /// just a reasonable anchor for "roughly how big will this be."</summary>
    private const double BppAtQuality0 = 0.02;
    private const double BppAtQuality100 = 0.35;

    /// <summary>Typical (expected) bitrate in kbps for the given resolution/frame rate/quality — the same
    /// bits-per-pixel model the guardrail's ceiling is built from, without the ceiling's headroom multiplier.
    /// Used for the Record screen's "~N MB/min" estimate.</summary>
    public static int EstimateTypicalKbps(int width, int height, int fps, int quality)
    {
        double t = Math.Clamp(quality, 0, 100) / 100.0;
        double bpp = BppAtQuality0 + t * (BppAtQuality100 - BppAtQuality0);
        double bps = bpp * width * height * fps;
        return Math.Max(200, (int)Math.Round(bps / 1000.0));
    }

    /// <summary>Generous <c>-maxrate/-bufsize</c> ceiling for the optional bitrate guardrail — well above the
    /// typical bitrate for the chosen quality/resolution/fps, so it only engages for unusually complex content
    /// (fast motion, busy screen updates), not normal recording.</summary>
    private const double GuardrailHeadroomMultiplier = 3.0;

    /// <summary>Hard ceiling on the computed guardrail maxrate. Found via a real failure: at high
    /// resolution/frame-rate/quality combos (e.g. 1702×1260@60, near-max quality) the uncapped estimate can
    /// exceed 100 Mbps, which SVT-AV1 rejects outright ("The maximum bit rate must be between [0, 100000]
    /// kbps") — the encoder then fails to open at all, breaking the recording immediately (not just a
    /// theoretical concern; this is the exact bitrate that triggered it). No real screen recording needs a
    /// ceiling above this anyway, so clamping is a pure safety net, not a quality trade-off in practice.</summary>
    private const int MaxRateCeilingKbps = 100_000;

    public static (int MaxRateKbps, int BufSizeKbps) EstimateGuardrail(int width, int height, int fps, int quality)
    {
        int typicalKbps = EstimateTypicalKbps(width, height, fps, quality);
        int maxRateKbps = Math.Clamp((int)Math.Round(typicalKbps * GuardrailHeadroomMultiplier), 500, MaxRateCeilingKbps);
        return (maxRateKbps, maxRateKbps * 2);
    }

    /// <summary>A qualitative name for where a 0–100 quality value sits — friendlier than a bare CRF number for
    /// non-technical users, alongside the estimated size (see <see cref="EstimateTypicalKbps"/>). Boundaries are
    /// judgment calls, not measured thresholds: picked to roughly track the curved CRF mapping's own inflection
    /// (each tier is where a materially different "why would I pick this" story applies), not to divide the
    /// slider into equal-width bands.</summary>
    public static string QualityTier(int quality) => quality switch
    {
        <= 25 => "Small file",
        <= 55 => "Balanced",
        <= 85 => "High quality",
        _ => "Visually lossless",
    };

    private static string BuildEncoderArgs(FfmpegJob job)
    {
        EncoderInfo encoder = job.Encoder;
        EncoderEffort effort = job.Effort;
        int value = EffectiveQualityValue(encoder, job.Quality);
        string c = value.ToString(CultureInfo.InvariantCulture);

        string args = encoder.FfmpegId switch
        {
            "libx264" => $"-c:v libx264 -preset {X264Preset(effort)} -crf {c}",
            "libx265" => $"-c:v libx265 -preset {X264Preset(effort)} -crf {c}",
            // asm=avx2 caps SVT-AV1's SIMD dispatch below its default auto-detected AVX-512 kernels. Found via
            // a real bug report: AV1 recordings on a VM came out solid black while H.264/HEVC on the same VM
            // (and AV1 on real hardware) were fine — some hypervisors expose AVX-512 CPUID flags without
            // correctly virtualizing every AVX-512 instruction, and SVT-AV1's AVX-512 code path silently
            // corrupts output (to black) instead of crashing, rather than falling back. AVX2 is mature and
            // near-universally correct under virtualization; the encode-speed cost versus AVX-512 is modest,
            // and reliability matters more than that margin for a screen recorder. Confirmed accepted by the
            // bundled SVT-AV1 build (logs "[asm level selected : up to avx2]" instead of "avx512icl").
            "libsvtav1" => $"-c:v libsvtav1 -preset {SvtAv1Preset(effort)} -crf {c} -svtav1-params asm=avx2",

            "h264_nvenc" or "hevc_nvenc" or "av1_nvenc" =>
                $"-c:v {encoder.FfmpegId} -preset {NvencPreset(effort)} -rc vbr -cq {c}",

            // AMF: cqp is the simplest honest mapping for Phase 1 (qvbr tuning comes in Phase 3).
            "h264_amf" or "hevc_amf" or "av1_amf" =>
                $"-c:v {encoder.FfmpegId} -usage transcoding -quality {AmfQuality(effort)} -rc cqp -qp_i {c} -qp_p {c}",

            "h264_qsv" or "hevc_qsv" or "av1_qsv" =>
                $"-c:v {encoder.FfmpegId} -preset {QsvPreset(effort)} -global_quality {c}",

            _ => $"-c:v {encoder.FfmpegId} -crf {c}",
        };

        if (job.BitrateGuardrailEnabled && SupportsBitrateGuardrail(encoder.Backend))
        {
            (int maxRateKbps, int bufSizeKbps) = EstimateGuardrail(job.Width, job.Height, job.FrameRate, job.Quality);
            args += $" -maxrate {maxRateKbps}k -bufsize {bufSizeKbps}k";
        }

        return args;
    }

    // Per-encoder effort → preset. Balanced deliberately keeps each encoder's established default.
    private static string X264Preset(EncoderEffort e) => e switch
    {
        EncoderEffort.Fast => "ultrafast",
        EncoderEffort.Quality => "medium",
        _ => "veryfast",
    };

    private static string SvtAv1Preset(EncoderEffort e) => e switch
    {
        EncoderEffort.Fast => "10",
        EncoderEffort.Quality => "6",
        _ => "8",
    };

    private static string NvencPreset(EncoderEffort e) => e switch
    {
        EncoderEffort.Fast => "p2",
        EncoderEffort.Quality => "p6",
        _ => "p4",
    };

    private static string AmfQuality(EncoderEffort e) => e switch
    {
        EncoderEffort.Fast => "speed",
        EncoderEffort.Quality => "quality",
        _ => "balanced",
    };

    // QSV presets are veryfast..veryslow (no "ultrafast").
    private static string QsvPreset(EncoderEffort e) => e switch
    {
        EncoderEffort.Fast => "veryfast",
        EncoderEffort.Quality => "slow",
        _ => "medium",
    };
}
