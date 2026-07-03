# RecMode Design Notes

Intentional deviations from `DOCS/RecMode Screen Recording App/RecMode.dc.html` live here so the design file can remain the visual source of truth while the product plan captures practical engineering decisions.

## Known Deviations

- Portable-first distribution: settings/logs/library state live in `.\Data`, and portable recordings default to `.\Recordings`.
- Performance controls: RecMode adds CPU thread caps, encoder effort controls, and process-priority settings not shown in the design prototype.
- Audio defaults: Opus is first-class for MKV/WebM, while AAC remains default for MP4/MOV compatibility.
- Encoder lists are hardware-dependent: vendor-specific entries appear only after probing and may carry beta labels until real hardware validation passes.
- CLI and single-instance automation are product features but are not represented in the visual prototype.
