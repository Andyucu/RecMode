# RecMode — Project Memory

Modern Windows 11 screen recorder (Bandicam-class). **.NET 10 · WPF · Fluent design · MVVM.**
**Win11 is primary; Win10 2004+ must work. Portable-first: ships as a self-contained folder/zip.**

## Source-of-truth documents
- **Implementation plan (read first):** `DOCS/IMPLEMENTATION_PLAN.md` (v2) — phases, architecture, decision gates, acceptance criteria.
- **Design (pixel reference):** `DOCS/RecMode Screen Recording App/RecMode.dc.html` — interactive prototype; the `<script type="text/x-dc">` block at the bottom defines every behavior (state machine, bitrate model, combos, hotkeys). Design system tokens/components: `DOCS/RecMode Screen Recording App/_ds/`.
- **Changelog:** `CHANGELOG.md` — update on every meaningful change (Keep a Changelog format).
- **Build log (engineer's notebook):** `PROJECT_MEMORY.md` — per-session record of what was actually built, decided, and learned (gotchas, file inventory, next actions). Newest-at-top; append every work session.
- Original claude.ai design project: https://claude.ai/design/p/e41a0a29-7166-4ab1-be7b-f8070754391f (already exported locally; `claude_design` MCP needs `/design-login` in an interactive session if re-import is ever needed).

## Key decisions (details + rationale in the plan §3)
- **Resource efficiency is a core product value (§3.9 — binding rules, violations are bugs):** nothing runs unless visible or recording (capture/audio/timers torn down on nav-away/minimize/tray); event-driven never polled; allocation-free hot paths (texture ring, ArrayPool, profiler-verified); one GPU copy chain (NV12, no BGRA round-trips); UI updates throttled+batched (meters ≤30 Hz, stats ≤4 Hz, preview ≤30 fps default, occlusion-paused); hw encoders default; virtualized/cached library; ReadyToRun. Measured budgets (plan §1): tray idle <0.5% CPU / <180 MB; Record screen open <3% CPU; recording 1440p60 hw = app <15% of one core excl. ffmpeg, memory flat over 2 h. Soak harness regression-checks these.
- **Portable-first (§3.5):** self-contained win-x64 publish; `portable.marker` + `AppPaths` service; all state in `.\Data\`; ffmpeg bundled at `.\ffmpeg\`; recordings default `.\Recordings`; nothing written outside the folder (opt-in exceptions labeled). Installer/auto-update (Velopack) is a later build sharing the same code.
- Capture: Windows.Graphics.Capture + D3D11 (Vortice); region = GPU crop; preview via D3DImage.
- Encoding: ffmpeg.exe subprocess over named pipes, **two-tier strategy with a measured decision gate (§3.3)** — Tier 1 = GPU NV12 convert → readback → pipe (MVP); Tier 2 = in-proc hw frames only if the gate fails. Encoders NVENC/AMF/QSV/x264/x265/SVT-AV1 probed at startup; containers MP4/MKV/MOV/WebM; quality slider → CRF = 51 − q·0.38. Encoder currency (plan §3.3): SVT-AV1 **screen-content mode ON for screen/window/region sources** (palette + intra-block-copy = sharper text/UI); **Opus is first-class audio** (default MKV/WebM; AAC = native ffmpeg encoder, default MP4/MOV since libfdk_aac isn't redistributable); ffmpeg re-pinned to latest stable at least quarterly with snapshot/ffprobe tests guarding drift; VVC + VP9 deliberately excluded.
- **Resource controls (§3.3):** CPU thread cap + effort presets (sw), effort tiers (NVENC p1–p7 / AMF / QSV), encoder process priority — bounded by hardware-derived recommendations; GPU % can't be hard-capped, effort tier is the honest lever.
- Audio: NAudio WASAPI (system loopback + mic) + process-loopback interop for per-app audio (post-MVP); 48 kHz f32 mixer, per-source gain/mute/meters; AAC/FLAC/Opus.
- MVVM: CommunityToolkit.Mvvm; DI: Microsoft.Extensions.Hosting; logging: Serilog; tray: H.NotifyIcon.
- Error taxonomy in Core from day one (§3.6): RecoverableWarning / BlockingError / DegradedState / FatalFinalizationError.
- Theming: port `_ds/tokens/*.css` to Light/Dark dictionaries + 5 accent presets; Mica on Win11 (opaque fallback Win10); 4px grid, 32px controls; Fluent System Icons as XAML geometries; sentence case, `×`, no emoji.
- **Robustness/product pass (plan v2 final):** CFR output policy (duplicate/drop vs QPC clock; VFR post-1.0); **HDR tone-map to SDR inside the NV12 pass** (washed-out-recording bug prevented, HDR passthrough post-1.0); global crash handler + local minidumps; black-frame watchdog for exclusive-fullscreen games; auto-split (FAT32 4 GB aware); CLI + single-instance forwarding (`--record/--screenshot/--stop/--tray`); all strings in .resx from Phase 1; **no telemetry, ever** (privacy is a feature); AMF/QSV beta-labeled until tested on real AMD/Intel hardware; dogfooding from M3; `DOCS/DESIGN_NOTES.md` logs deviations from the design file (starts Phase 6); **post-1.0 backlog is plan §7** (replay buffer ranked #1) — nothing from it creeps into 1.0.
- **Feature-list triage (2026-07-03, user's idea list):** promoted into 1.0 — safe-recording mode (MKV + auto-remux to MP4, default on, Phase 3), recording health indicator (dropped frames/encoder load/disk speed, Phase 3), pre-flight checks on every start path (§3.6, Phase 5), capture-source metadata in the library index from day one (Phase 5), tray quick-record + recent targets (Phase 6), battery warning (Phase 9), first-run encoder benchmark→recommended defaults (Phase 10), DRM-window warning folded into black-frame watchdog. Everything else went to the **grouped §7 backlog** (editing suite, annotation pro, library pro incl. possible SQLite graduation, audio DSP chain, auto-pause, hooks, privacy guard). Mic test = the live meters; low-motion mode noted as mostly inherent to encoders.
- **MVP 1.0-alpha cut (plan §1):** monitor+region, H.264 MP4/MKV, system+mic audio, pause, screenshots, basic library, tray+hotkeys, portable zip. Deferred past MVP: per-app audio, webcam, annotation, scheduler, full AV1/HEVC matrix, MOV/WebM, compact launcher, share polish.

## Status tracker (keep current — mark [x] when a phase's acceptance criteria pass)
- [x] Design created and exported to DOCS
- [x] Implementation plan v1 written; **v2 revision** (spike, decision gate, MVP cut, tests, error taxonomy, OS matrix, portable-first, resource controls) — 2026-07-03
- [x] Project memory + changelog created
- [x] **Phase 0 — Foundation (2026-07-04):** git init; `RecMode.slnx` (6 src + 3 test projects); `Directory.Build.props` + central package mgmt; DI host (M.E.Hosting) + Serilog; Core error taxonomy, recording state machine (pause-PTS/gapless), versioned settings service (migration + corrupt recovery), `AppPaths` (portable), `OsCapabilities`, crash reporter + opt-in minidump; `FfmpegLocator` (bundled/override + SHA-256 manifest verify); WPF shell window (DI'd); `build.ps1` + `publish-portable.ps1` + `portable.marker`. **Acceptance met:** `dotnet build` clean (0 warnings), `dotnet test` 33/33 green, themed window launches in portable mode, ffmpeg resolution runs (binaries not yet staged → graceful "not found"). Details in `PROJECT_MEMORY.md`.
- [ ] Phase 0.5 — Pipeline spike ⚠️ gate: WGC→D3D11→NV12→pipe→ffmpeg H.264→MP4/MKV + system audio, 5-min no-drift, throughput measured, kill-test → **record gate outcome here**
- [ ] Phase 1 — Minimal shell + Record essentials (functional, not pixel-perfect; sidebar only)
- [ ] Phase 2 — Capture engine: monitor/window/region/all-displays, preview, region overlay
- [ ] Phase 3 — Encoding productionized ⚠️ gate re-check: state machine, pause PTS, fallback chain, crash safety, resource-control args
- [ ] Phase 4 — Audio core: system+mic mixer, meters, A/V sync ±40 ms
- [ ] Phase 5 — MVP UX → 🏁 **MVP 1.0-alpha**: hotkeys, tray, countdown, toolbar (capture-excluded), screenshots, basic library, failure-mode UX, portable acceptance (USB test)
- [ ] Phase 6 — Full design fidelity: all tokens/accents, topbar+compact layouts, Library/Schedule/Settings per design, motion
- [ ] Phase 7 — Full codec matrix + per-app audio + webcam (source & overlay)
- [ ] Phase 8 — Annotation, click ripple, scheduler
- [ ] Phase 9 — Settings completion: patterns, hotkey remap UI, Performance group, startup, update-notify
- [ ] Phase 10 — Hardening, a11y, Win10 regression pass, portable zip 1.0.0 (installer = stretch)

## Working notes
- Git repo initialized (Phase 0). **Local commits only — do NOT push to GitHub until the user says so.**
- **Next: Phase 0.5 pipeline spike (⚠️ decision gate).** Prereqs: add Vortice.Windows + NAudio packages; **stage real ffmpeg binaries in `tools\ffmpeg\`** (+ generate `ffmpeg.manifest.json` with SHA-256 per `ffmpeg/README.md`). Spike proves WGC→D3D11→NV12→pipe→ffmpeg h264_nvenc→MP4/MKV + system audio, 5-min no-drift.
- Phase 0.5 spike results (throughput MB/s, CPU %, dropped frames, kill-test findings) must be written into this file **and** `PROJECT_MEMORY.md` when measured.
- Build/test: `./build.ps1` (Debug) or `./build.ps1 -Configuration Release`. Portable zip: `./publish-portable.ps1`.
- Gotcha: solution file is `.slnx` (not `.sln`); app.manifest `<assembly>` root must be well-formed or the exe fails to start with a SxS error.
- When implementing UI, open `RecMode.dc.html` in a browser next to the app and match both themes.
