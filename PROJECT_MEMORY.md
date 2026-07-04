# RecMode — Project Memory (build log)

> Running log of what was actually built, decided, and learned during implementation.
> Distinct from `CLAUDE.md` (decision log + phase checklist) and `CHANGELOG.md` (user-facing Keep-a-Changelog).
> This file is the engineer's notebook: every work session appends here with concrete file/state changes so a cold start can pick up instantly.
>
> Convention: newest session at the top. Reference files as `path:line`. Keep entries factual.

---

## Session 2026-07-04 — Phase 0.5: Pipeline spike ⚠️ GATE PASSED

**Goal:** Prove the whole risky spine end-to-end and evaluate the §3.3 throughput decision gate before
any Phase 1 UI. Disposable code in `tools/spike/` (not in `RecMode.slnx`); keep the learnings.

### Hardware reality (important)
- Dev machine is **AMD**: Radeon RX 7900 XTX (24 GB) + Ryzen 7 7700X (8C/16T). Primary display is a
  **5120×1440 ultrawide** (~7.4 Mpix, 2× the pixels of 1440p). There's also a Parsec virtual display
  adapter + the 7700X iGPU; adapter picker chooses max-VRAM (the 7900 XTX). Session is Console/Interactive.
- **So the hw encoder path validated is `h264_amf`, not `h264_nvenc`** (the plan assumed NVENC). AMF was
  "beta until real AMD hardware" — this is that validation. NVENC/QSV still need their own gate checks.

### What was built (tools/spike/)
- `Interop.cs` — pick discrete adapter; D3D11 device w/ BGRA+Video support (Vortice); WinRT `IDirect3DDevice`
  via `CreateDirect3D11DeviceFromDXGIDevice` + `MarshalInterface<T>.FromAbi`; WGC item for primary monitor
  via `RoActivationFactory`→`IGraphicsCaptureItemInterop.CreateForMonitor`; surface→`ID3D11Texture2D` via
  `IDirect3DDxgiInterfaceAccess`.
- `Nv12Converter.cs` — D3D11 **VideoProcessor** BGRA→NV12 (with optional scale) in one GPU pass; NV12
  staging texture readback, **de-padded to tightly-packed NV12** for the pipe.
- `Recorder.cs` — CFR pacing thread (QPC, `timeBeginPeriod(1)` + sleep/spin) writes latest NV12 to a
  `NamedPipeServerStream`; launches ffmpeg reading `\\.\pipe\...`; optional 2nd audio pipe; measurement.
- `AudioLoopback.cs` — NAudio `WasapiLoopbackCapture`; wall-clock-paced writer that **pads silence** (WASAPI
  delivers nothing during silence → would desync) to keep audio aligned.
- `Program.cs` — modes: `probe`, `record`, `recordav`.

### Measurements (the gate)
- **1440p60 h264_amf, 5-min:** paced **17,992/18,000** CFR frames (0 steady-state drops), **316 MB/s**
  sustained (94.8 GB total), **app CPU 10.2% of one core** excl. ffmpeg, memory **flat ~108 MB peak**.
  Output valid (h264 2560×1440, 60/1 fps, 299.87s). ffmpeg's own cost ≈1.2 cores (separate process).
- **Matrix:** h264_amf & libx264 × mp4 & mkv → all ffprobe-valid.
- **Headroom:** native 5120×1440 libx264 = **620 MB/s, 0 stalls, 18.9% one core** (pipeline not the limit).
- **Audio:** loopback → AAC muxed; A/V duration align ~**49 ms**.
- **Kill test:** MP4 killed mid-record → **dead (no moov)**; MKV killed → **recoverable**, remuxed `-c copy`
  → clean MP4 (5.8s). Proves safe-recording (MKV+remux) design.

### Gate verdict
**Tier 1 (NV12-first → readback → pipe) PASSES on AMD AMF at 1440p60. No Tier 2 needed for MVP.**

### Gotchas / learnings (for Phase 1–3 productionization)
- **Multi-input pipe deadlock:** ffmpeg opens input 0 (video), then blocks in `find_stream_info` reading a
  frame *before* opening input 1 (audio). If you wait for the audio pipe to connect before writing video →
  deadlock. Fix: after the video pipe connects, **start video pacing immediately**; wait for the audio
  pipe + run its pump on a separate thread.
- **h264_amf max width = 4096** (H.264 level cap); 5120-wide → "Invalid argument". Ultrawide/5K needs
  HEVC/AV1 or downscale. (Our default output was 2560×1440 scaled, which sidesteps this.)
- **AMF encoder-init stall:** the first pipe write blocks ~95–105 ms while AMF initializes. One-time, at
  t=0, before steady state — harmless, but production should init the encoder before the first real frame.
- **ffmpeg death → `pipe.Write` throws `IOException` ("Pipe is broken")**. The spike just crashes; production
  must catch at the pipe boundary, map to `FatalFinalizationError`, stop cleanly, attempt MKV recovery.
- WGC delivers ~52 unique fps on a static desktop; CFR pacing duplicates to exactly 60 → 60/1 in output.
- Vortice quirks: `EnumAdapters1` index is `uint`; content-desc dims & `Texture2DDescription` W/H are `uint`;
  `VideoProcessorBlt(proc, outView, 0, streamCount, streams[])` needs the explicit stream count.

### ffmpeg staging
- Downloaded BtbN `ffmpeg-master-latest-win64-gpl` (159.8 MB) → staged `ffmpeg.exe`/`ffprobe.exe` in
  `tools/ffmpeg/` (binaries gitignored). Wrote **`ffmpeg.manifest.json`** with SHA-256 (committed) so
  `FfmpegLocator` hash-verify passes. Confirmed encoders: h264/hevc/av1_amf, libx264/265, libsvtav1, aac, libopus.

### Next actions
- Phase 1: port Tier-1 learnings into `RecMode.Capture` (WGC + VideoProcessor NV12 + CFR) and
  `RecMode.Encoding` (pipe + ffmpeg session builder), behind minimal real UI. Handle the pipe-death path.

---

## Session 2026-07-03 — Phase 0: Foundation & scaffolding

**Goal:** Stand up the solution, DI host, Core services (error taxonomy, state machine skeleton,
settings, AppPaths, OsCapabilities), ffmpeg resolution, portable publish scaffold, crash handler,
and green build + tests. No GitHub push (user will authorize later).

### What exists at session start
- Docs only: `CLAUDE.md`, `CHANGELOG.md`, `DOCS/IMPLEMENTATION_PLAN.md` (v2), `DOCS/DESIGN_NOTES.md`,
  design prototype under `DOCS/RecMode Screen Recording App/`.
- Toolchain: .NET SDK 10.0.301 (WPF via Windows Desktop SDK), git 2.55, dotnet workloads none needed.
- No git repo, no code.

### Decisions made this session
- **Central Package Management** (`Directory.Packages.props`) — versions pinned in one place. Phase 0 deps:
  CommunityToolkit.Mvvm 8.4.0, Microsoft.Extensions.Hosting 10.0.0, Serilog 4.2.0 (+File/Async/Hosting),
  xunit 2.9.2, Microsoft.NET.Test.Sdk 17.12.0.
- **Solution is `.slnx`** (new XML format; `dotnet new sln` emits it by default on SDK 10). Build/publish
  scripts target `RecMode.slnx`.
- **All csprojs are minimal** — shared TFM/nullable/analyzers/x64 come from `Directory.Build.props`
  (`net10.0-windows10.0.19041.0`, the Win10-2004 floor). `WarningsAsErrors=nullable` only; analyzers at
  `latest-recommended` with a few CA rules dialed down in `.editorconfig` (CA1031/CA1716/CA1305; CA1707
  off for tests via `tests/.editorconfig`).
- **App entry**: standard WPF generated `Main` (no custom `Program`/`StartupObject`); all bootstrapping in
  `App.OnStartup`. Single-instance/CLI front door is deferred to Phase 5 as planned.
- **`[ObservableProperty]` partial-property generator didn't emit** for the Phase-0 set-once shell strings,
  so `ShellViewModel` uses plain get-only auto-properties. Revisit when real observable state lands (Phase 1).
- **Portable by default in dev too**: `portable.marker` is `CopyToOutputDirectory`, so `bin\...\Data\`
  holds dev state. Keeps everything self-contained; `.gitignore` excludes `Data/` and `Recordings/`.
- **ffmpeg binaries are NOT committed** (large + licensing). `FfmpegLocator` resolves bundled `.\ffmpeg\`
  or a user override, and verifies SHA-256 against `ffmpeg.manifest.json` when present. Acquisition +
  manifest instructions in `ffmpeg/README.md`; `publish-portable.ps1` copies from `tools\ffmpeg\`.
- **Crash detection = session sentinel**: `CrashReporter` writes `Data\logs\crash\session.open` at startup,
  deletes it on clean exit; if it survives to next launch → `PreviousSessionCrashed`. Verified live (force-kill
  left the marker). Minidump writer is `dbghelp!MiniDumpWriteDump` in Interop, gated on the opt-in setting.

### Files created / changed (Phase 0)
- Repo config: `.gitignore`, `.editorconfig`, `tests/.editorconfig`, `Directory.Build.props`,
  `Directory.Packages.props`, `RecMode.slnx`, `build.ps1`, `publish-portable.ps1`.
- `RecMode.Core`: `Errors/` (ErrorSeverity, RecModeError, IErrorReporter+ext, ErrorReporter),
  `Recording/` (RecordingState, RecordingStateMachine, IMonotonicClock+Stopwatch/Manual, StateChanged args),
  `Settings/` (enums, RecModeSettings, SettingsMigrator, ISettingsService, SettingsService),
  `Infrastructure/` (IAppPaths+AppPaths, IOsCapabilities+OsCapabilities, ICrashReporter+CrashReporter,
  IMinidumpWriter+NullMinidumpWriter).
- `RecMode.Interop`: `Diagnostics/MinidumpWriter.cs`.
- `RecMode.Encoding`: `Ffmpeg/` (FfmpegResolution, FfmpegManifest, IFfmpegLocator, FfmpegLocator).
- `RecMode.App`: `app.manifest` (PerMonitorV2, Win10/11 supportedOS), `App.xaml(.cs)`, `Composition.cs`,
  `ViewModels/ShellViewModel.cs`, `Views/ShellWindow.xaml(.cs)`, `portable.marker`,
  `Properties/PublishProfiles/Portable.pubxml`.
- Tests: Core.Tests (state machine, settings, AppPaths, OsCapabilities, error taxonomy, TestDoubles),
  Encoding.Tests (FfmpegLocator), Recording.Tests (placeholder).
- Scaffold: `ffmpeg/README.md`, `licenses/README.md`.

### Build / test status
- `dotnet build -c Release`: **0 warnings, 0 errors**. `dotnet test`: **33 passed / 0 failed**.
- App launches in portable mode; creates `Data\logs`, writes daily log, drops `session.open`. SxS manifest
  bug (stray `manifest` token in `<assembly>` root) found + fixed during live launch test.

### Gotcha log (for next cold start)
- `<assembly>` manifest root must NOT have a stray attribute; a malformed manifest → "side-by-side
  configuration is incorrect" at process start (build still succeeds). Watch this if editing app.manifest.
- `dotnet new sln` produces `.slnx`, not `.sln` — scripts and commands must use the right name.

### Open questions / next actions
- **Next: Phase 0.5 pipeline spike** (⚠️ decision gate) — WGC→D3D11→NV12→pipe→ffmpeg h264_nvenc→MP4/MKV +
  system audio, 5-min no-drift, measure throughput/CPU/dropped frames, kill-test. Needs Vortice.Windows +
  NAudio packages and **real ffmpeg binaries staged in `tools\ffmpeg\`** (+ manifest). Record gate outcome
  in CLAUDE.md.
- Consider committing a real `ffmpeg.manifest.json` once binaries are pinned.
