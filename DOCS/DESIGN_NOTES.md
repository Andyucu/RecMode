# RecMode Design Notes

Intentional deviations from `DOCS/RecMode Screen Recording App/RecMode.dc.html` live here so the design file can remain the visual source of truth while the product plan captures practical engineering decisions.

## Phase 6 — Schedule screen (2026-07-05, part 2)

- **Per-card Delete button added.** The design's schedule card shows only a name/description, state label, and an on/off toggle. RecMode adds a Delete action so schedules can be removed. (The design's "New schedule" also has no editor; see below.)
- **No per-schedule editor yet.** "New schedule" adds a default once-off 30 minutes from now (matching the prototype, which likewise just appends a default). Editing name/recurrence/time/duration will land with the firing engine (Phase 8) or a dedicated edit dialog. The data model (`ScheduleItem`: recurrence, time, duration, enabled) already persists everything the engine will need.
- **Card icons omitted** — same as Settings; pending the Fluent icon-geometry set.

## Phase 6 — Settings screen (2026-07-05, part 1)

- **Card icons omitted for now.** The design's settings cards carry a leading Fluent SVG icon (paint brush, video, timer, …). RecMode has no XAML icon-geometry system yet, so cards ship as title + description + control without the leading glyph. Structure, grouping, and controls match. **Follow-up:** add the Fluent System Icons geometry set (a Phase 6 item) and slot icons into the `SettingsCard`.
- **Theme & accent use combo boxes, not a segmented control / colour swatches.** The design shows a segmented Light/Dark control and five accent colour circles; the app reuses the existing `AppComboBox` (as on the Record screen) for consistency and lower risk. **Follow-up:** segmented theme control + accent swatch row.
- **Enum labels come from `ToString()`** (e.g. `Av1`, `Mp4`, `WebM`, `Aac`) rather than the design's polished casing (`AV1`, `MP4`, `AAC`). **Follow-up:** friendly-name converters when the codec matrix lands (Phase 7).
- **Hotkeys are read-only key caps.** The design has a per-shortcut "Change" affordance; remapping is Phase 9.

## Known Deviations

- Portable-first distribution: settings/logs/library state live in `.\Data`, and portable recordings default to `.\Recordings`.
- Performance controls: RecMode adds CPU thread caps, encoder effort controls, and process-priority settings not shown in the design prototype.
- Audio defaults: Opus is first-class for MKV/WebM, while AAC remains default for MP4/MOV compatibility.
- Encoder lists are hardware-dependent: vendor-specific entries appear only after probing and may carry beta labels until real hardware validation passes.
- CLI and single-instance automation are product features but are not represented in the visual prototype.

## Engineering notes (not design deviations, but decisions worth recording)

- **Dev/spike hardware is AMD, not NVIDIA.** The Phase 0.5 pipeline spike was validated on an AMD Radeon RX 7900 XTX (Ryzen 7 7700X). The plan wrote the decision gate around `h264_nvenc`; on this machine the hardware path is `h264_amf`. This means the AMF path (which the plan flagged "beta until tested on real AMD hardware") got real-hardware validation first. NVENC/QSV still need their own smoke tests on NVIDIA/Intel machines (plan §3.8, Phase 7).
