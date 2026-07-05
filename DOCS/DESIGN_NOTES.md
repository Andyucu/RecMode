# RecMode Design Notes

Intentional deviations from `DOCS/RecMode Screen Recording App/RecMode.dc.html` live here so the design file can remain the visual source of truth while the product plan captures practical engineering decisions.

## Phase 6 — Schedule screen (2026-07-05, part 2)

- **Per-card Delete button added.** The design's schedule card shows only a name/description, state label, and an on/off toggle. RecMode adds a Delete action so schedules can be removed. (The design's "New schedule" also has no editor; see below.)
- ~~No per-schedule editor yet.~~ **Resolved 2026-07-05:** a modal edit dialog (`ScheduleEditWindow`/`IScheduleEditor`) edits name, recurrence, start time (HH:mm, validated), and duration. "New schedule" opens it immediately; each card also has an Edit button. (This is a functional addition beyond the prototype, which only appends a default.)
- **Card icons omitted** — same as Settings; pending the Fluent icon-geometry set.

## Phase 6 — Settings screen (2026-07-05, part 1)

- **Icons: `Segoe Fluent Icons` font, not embedded SVGs.** RecMode renders the design's icon language via the Windows `Segoe Fluent Icons` (fallback `Segoe MDL2 Assets`) font glyphs — the mechanism the source tiles already use. **Nav sidebar icons done 2026-07-05** (Record=video, Library=grid, Schedule=calendar, Settings=gear). **Still pending:** per-card leading icons on the Settings/Schedule cards (a curated glyph pass across ~14 cards); cards currently ship title + description + control.
- ~~Theme & accent use combo boxes, not a segmented control / colour swatches.~~ **Resolved 2026-07-05:** Appearance now uses a segmented System/Light/Dark selector and five accent colour swatches (with a selection ring), matching the design. Both apply live and persist (`EnumToBoolConverter` binds RadioButtons to the enum settings).
- ~~Enum labels come from `ToString()`~~ **Resolved 2026-07-05:** an `EnumDisplayConverter` renders friendly labels (H.264 / HEVC / AV1 / MP4 / MKV / MOV / WebM / AAC / Opus / FLAC) in the encoding-defaults and Record Format combos while the stored value stays a plain enum.
- **Hotkeys are read-only key caps.** The design has a per-shortcut "Change" affordance; remapping is Phase 9.

## Known Deviations

- Portable-first distribution: settings/logs/library state live in `.\Data`, and portable recordings default to `.\Recordings`.
- Performance controls: RecMode adds CPU thread caps, encoder effort controls, and process-priority settings not shown in the design prototype.
- Audio defaults: Opus is first-class for MKV/WebM, while AAC remains default for MP4/MOV compatibility.
- Encoder lists are hardware-dependent: vendor-specific entries appear only after probing and may carry beta labels until real hardware validation passes.
- CLI and single-instance automation are product features but are not represented in the visual prototype.

## Engineering notes (not design deviations, but decisions worth recording)

- **Dev/spike hardware is AMD, not NVIDIA.** The Phase 0.5 pipeline spike was validated on an AMD Radeon RX 7900 XTX (Ryzen 7 7700X). The plan wrote the decision gate around `h264_nvenc`; on this machine the hardware path is `h264_amf`. This means the AMF path (which the plan flagged "beta until tested on real AMD hardware") got real-hardware validation first. NVENC/QSV still need their own smoke tests on NVIDIA/Intel machines (plan §3.8, Phase 7).
