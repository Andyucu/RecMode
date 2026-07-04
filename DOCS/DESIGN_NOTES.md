# RecMode Design Notes

Intentional deviations from `DOCS/RecMode Screen Recording App/RecMode.dc.html` live here so the design file can remain the visual source of truth while the product plan captures practical engineering decisions.

## Known Deviations

- Portable-first distribution: settings/logs/library state live in `.\Data`, and portable recordings default to `.\Recordings`.
- Performance controls: RecMode adds CPU thread caps, encoder effort controls, and process-priority settings not shown in the design prototype.
- Audio defaults: Opus is first-class for MKV/WebM, while AAC remains default for MP4/MOV compatibility.
- Encoder lists are hardware-dependent: vendor-specific entries appear only after probing and may carry beta labels until real hardware validation passes.
- CLI and single-instance automation are product features but are not represented in the visual prototype.

## Engineering notes (not design deviations, but decisions worth recording)

- **Dev/spike hardware is AMD, not NVIDIA.** The Phase 0.5 pipeline spike was validated on an AMD Radeon RX 7900 XTX (Ryzen 7 7700X). The plan wrote the decision gate around `h264_nvenc`; on this machine the hardware path is `h264_amf`. This means the AMF path (which the plan flagged "beta until tested on real AMD hardware") got real-hardware validation first. NVENC/QSV still need their own smoke tests on NVIDIA/Intel machines (plan §3.8, Phase 7).
