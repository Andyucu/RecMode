# RecMode — Project Memory (build log)

> Running log of what was actually built, decided, and learned during implementation.
> Distinct from `CLAUDE.md` (decision log + phase checklist) and `CHANGELOG.md` (user-facing Keep-a-Changelog).
> This file is the engineer's notebook: every work session appends here with concrete file/state changes so a cold start can pick up instantly.
>
> Convention: newest session at the top. Reference files as `path:line`. Keep entries factual.

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
