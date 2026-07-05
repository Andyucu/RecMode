# RecMode — Project Memory (build log)

> Running log of what was actually built, decided, and learned during implementation.
> Distinct from `CLAUDE.md` (decision log + phase checklist) and `CHANGELOG.md` (user-facing Keep-a-Changelog).
> This file is the engineer's notebook: every work session appends here with concrete file/state changes so a cold start can pick up instantly.
>
> Convention: newest session at the top. Reference files as `path:line`. Keep entries factual.

---

## Session 2026-07-05 — Phase 6 (part 4): nav icons + friendly enum labels

**Goal:** Sidebar nav icons and design-cased enum labels (two logged deviations).

### What was built
- **Nav icons:** `NavButton` template gained a leading glyph `TextBlock` (`Segoe Fluent Icons, Segoe MDL2
  Assets`, 16px, bound to `Tag`). ShellWindow nav RadioButtons set `Tag`: Record `&#xE714;` (video),
  Library `&#xE8A9;` (grid), Schedule `&#xE787;` (calendar), Settings `&#xE713;` (gear). **Key point:** the
  app already used this font for source tiles — no SVG-geometry system needed.
- **`EnumDisplayConverter`** (Themes, one-way): maps enum → design label (H264→"H.264", Mp4→"MP4", Aac→"AAC",
  Av1→"AV1", …); unmapped → ToString. Registered as `EnumDisplay` + an `EnumItemTemplate` DataTemplate.
  Applied `ItemTemplate="{StaticResource EnumItemTemplate}"` to the Settings codec/container/audio-format
  combos and the Record Format combo (ComboBox closed display reuses the ItemTemplate → friendly in both states).

### Verification (real GUI, screenshots)
- Sidebar: four correct glyphs (video/grid/calendar/gear) in light theme.
- Settings encoding defaults now read "H.264" / "MP4" / "AAC" (were H264/Mp4/Aac). 54 tests, 0 warnings.

### Remaining for Phase 6
- Per-card leading icons (Settings/Schedule cards) — curated glyph pass; per-schedule editor; topbar + compact
  layouts; Library/Record polish + motion.

---

## Session 2026-07-05 — Phase 6 (part 3): Appearance controls (segmented theme + accent swatches)

**Goal:** Replace the Appearance combos with the design's segmented theme selector + accent colour swatches.

### What was built
- **`EnumToBoolConverter`** (Themes): two-way `IValueConverter` — `Convert` = `value.ToString()==param`;
  `ConvertBack` returns `Enum.Parse(targetType, param)` only when checked, else `Binding.DoNothing` (so the
  unchecked RadioButton in a group doesn't clobber selection). Registered in Controls.xaml as `EnumToBool`.
- **Styles** (Controls.xaml): `SegmentedButton` (RadioButton pill — accent fill + on-accent text when checked,
  subtle hover) and `AccentSwatch` (26px RadioButton — colour circle from `Background`, ring in TextPrimary
  when checked / StrokeControl on hover).
- **SettingsView Appearance**: theme card → 3 `SegmentedButton`s (System/Light/Dark, GroupName=theme) in a
  bordered SurfaceInput container; accent card → 5 `AccentSwatch`es (hardcoded hexes #0078D4/#D13438/#8B6CCB/
  #0F7B7B/#CA5010, GroupName=accent). Each `IsChecked="{Binding SelectedTheme/Accent, Converter=EnumToBool,
  ConverterParameter=<member>}"`.

### Verification (real GUI, UI-Automation)
- Screenshot: segmented System|Light|Dark (Light selected, accent-filled) + 5 swatches (selected has ring) —
  matches design. Clicking Light switched whole app to light theme.
- Two-way binding works: theme System→Light persisted (`Theme=Light`); teal swatch → accent recoloured live
  (segment fill + ring went teal) and persisted (`Accent=Teal`). 54 tests, 0 warnings.

### Gotchas
- Accent swatch RadioButtons have no Content/Name → not findable by UI-Automation name; click by screen
  position (window origin + offset). Theme segments have Content ("Light") so they're name-clickable.

### Remaining for Phase 6
- Fluent icon-geometry set + card/nav icons; per-schedule editor; topbar + compact layouts; Library/Record
  polish + motion; friendly enum names (Av1→AV1 etc.).

---

## Session 2026-07-05 — Phase 6 (part 2): Schedule screen

**Goal:** Turn the Schedule stub into the design's screen with a persisted data model (firing engine = Phase 8).

### What was built
- **Core:** `ScheduleItem` (Id, Name, `ScheduleRecurrence` {Once/Daily/Weekdays/Weekly}, Time "HH:mm",
  DurationMinutes, Enabled) + `List<ScheduleItem> Schedules` added to `RecModeSettings` (additive, no schema
  bump — serializes with the settings doc; save path serializes `Current` directly so the shallow `Clone()`
  sharing the list is a non-issue).
- **App:** `ScheduleRowViewModel` (wraps a `ScheduleItem`, `Enabled` setter persists via injected callback,
  `WhenText`="Once · 18:32 · 30 min", `SourceText`="Follows Record settings", `StateLabel`=On/Off).
  `ScheduleViewModel` (`INavigationAware`): loads rows from settings on nav, `NewScheduleCommand` (default
  once-off now+30m, persists), `DeleteCommand` (removes by Id, persists), `IsEmpty`.
- **View:** `ScheduleView.xaml` per design — title + `New schedule` accent button, subtext, `ItemsControl` of
  `SettingsCard` rows (name + when·source, On/Off label, `ToggleSwitch`, Delete via RelativeSource command),
  empty-state text. Strings: Schedule_Title/New/Subtext/Delete/NoItems.

### Verification (real GUI, UI-Automation)
- Screenshot matches design (header + subtext + two "Once · 18:32 · 30 min · Follows Record settings" cards,
  On + blue toggle + Delete).
- Persistence: New schedule ×2 → settings.json Schedules 0→2 (recurrence=Once, time=now+30, dur=30, enabled=true);
  toggle-off → `Enabled` true→false persisted; delete → 2→1. 54 tests, 0 warnings.

### Gotchas
- UI-Automation `FindAll` over templated `ItemsControl` returns phantom/recycled containers (delete-button count
  read as 4 for 2 rows) — same as the Library test; trust the persisted-state ground truth, not element counts.

### Remaining for Phase 6
- Fluent icon-geometry set + card icons; per-schedule editor (name/recurrence/time/duration); segmented theme +
  accent swatches; topbar + compact layouts; Library/Record polish + motion; friendly enum names.

---

## Session 2026-07-05 — Phase 6 (part 1): full Settings screen

**Goal:** Start Phase 6 (design fidelity) with the Settings page — from a 3-field stub to the design's full layout.

### What was built
- **`StartupManager`/`IStartupManager`** (App/Services): per-user HKCU `…\CurrentVersion\Run` value `RecMode`
  = `"<exe>" --tray`. `IsEnabled` reads the key; `SetEnabled` writes/deletes it. Registry is the source of
  truth for the toggle (not settings.json). The one deliberate write outside the portable folder (§3.5 opt-in).
- **`SettingsViewModel`** rewritten: all design settings wired with a generic `Persist(ref field, value, apply)`
  → each change writes `settings.Current.X` + `RequestSave()`. Props: theme/accent (live-apply), codec/
  container/audio-codec/audio-bitrate (enum+int combos), output folder + browse, filename pattern,
  countdown (bool↔CountdownSeconds 0/3), capture-cursor, highlight-clicks, start-with-windows (via
  StartupManager), check-for-updates; read-only hotkey strings; `VersionInfo` via entry-assembly version.
- **`SettingsView.xaml`** rebuilt to the design: `SectionHeader` + `SettingsCard` (title + desc + control)
  grouped into Appearance / Encoding defaults / Output / Recording / Hotkeys (key-cap chips) / General.
- **Styles** (Controls.xaml): `SectionHeader`, `SettingsCard`, `SettingsCardDesc`, and a real `AppTextBox`
  (templated, accent focus border) — none existed before.
- Strings: ~30 new `Settings_*` keys (resx + Strings.cs). `IStartupManager` registered in Composition.
- `DOCS/DESIGN_NOTES.md`: logged Phase-6 Settings deviations (card icons omitted; theme/accent use combos not
  segmented/swatches; enum labels via ToString; hotkeys read-only until Phase 9).

### Verification (real GUI, UI-Automation)
- All sections render correctly (two screenshots — top: Appearance/Encoding/Output; scrolled: Output/Recording
  toggles/Hotkeys). Toggles show accent state (countdown+cursor on, clicks off = defaults).
- Toggled Highlight-clicks + Start-with-Windows → `settings.json` shows `HighlightClicks=true`,
  `StartWithWindows=true`; **registry Run key** = `"…\RecMode.exe" --tray` (removed on cleanup). 54 tests, 0 warnings.

### Gotchas
- No `AppTextBox` / `SectionHeader` styles existed — added them. No icon-geometry system yet → cards are
  icon-less (design-notes deviation).
- Registry is authoritative for start-with-Windows; the settings.json bool mirrors it (init reads registry).

### Remaining for Phase 6
- Fluent icon-geometry set + card icons; segmented theme + accent swatches; Schedule screen per design; topbar
  + compact layouts; Library/Record polish + motion; friendly enum names.

---

## Session 2026-07-05 — Phase 5 done: portable USB acceptance → 🏁 MVP 1.0-alpha

**Goal:** The last Phase 5 item — verify the portable build is self-contained and folder-contained.

### What was done
- Ran `publish-portable.ps1 -Version 0.1.0` → `artifacts\RecMode-0.1.0-portable-win-x64\` (self-contained
  win-x64, R2R, bundled ffmpeg from `tools\ffmpeg`, `portable.marker`). Zip produced too.
- **Relocated** the folder to a path outside the repo (scratchpad `RecModeUSB`) to simulate a USB drive.
- Snapshotted the non-portable locations before: `%AppData%\RecMode`, `%LocalAppData%\RecMode`,
  `%Videos%\RecMode` — all **absent** (dev/bin runs are portable, so nothing had leaked there).

### Verification (all pass)
- `--selftest-record` (exit 0) → `Recordings\RecMode ….mp4` (567 KB) — **bundled ffmpeg resolved from `.\ffmpeg\`**.
- `--selftest-screenshot` (exit 0) → `Recordings\Screenshots\….png` (2 MB).
- `Data\logs\*.log` written inside the folder.
- Normal windowed launch boots the full R2R self-contained app (window shown).
- Theme toggle → `Data\settings.json` written **inside** the portable folder (`Theme=Light` persisted).
- **Non-portable locations stayed absent throughout** → nothing written outside the folder (§3.5 satisfied).

### Result
- **Phase 5 acceptance met → MVP 1.0-alpha reached.** MVP cut delivered: monitor+region, H.264 MP4/MKV,
  system+mic audio, pause, screenshots, basic library, tray+hotkeys, CLI/single-instance, countdown +
  recording toolbar, portable zip.
- `artifacts/` is gitignored; no source changed this session (docs only).

### Next
- Phase 6 (full design fidelity) or start the §7 backlog. Vendor gate re-checks (NVENC/QSV on NVIDIA/Intel)
  still outstanding. Consider wiring the library.json index + capture-source metadata when Library-pro lands.

---

## Session 2026-07-05 — Phase 5 (part 4): basic Library

**Goal:** Turn the Library stub into a working recordings + screenshots browser.

### What was built
- **`LibraryItem`** (VM model): FilePath, DisplayName, Meta (`size · date`), IsImage, Thumbnail (ImageSource?).
- **`LibraryViewModel`** (`INavigationAware`): filesystem-backed — enumerates `RecordingsDirectory`
  (`.mp4/.mkv/.mov/.webm`, excludes `*.recording.mkv`) or `ScreenshotsDirectory` (`.png/.jpg/.jpeg`),
  newest-first. Videos/Screenshots tabs (`ShowVideos`/`ShowScreenshots`), `IsEmpty`/`EmptyMessage`. Commands:
  ShowVideos/ShowScreenshots, Refresh, OpenFolder, and per-item Open (`ShellExecute`), Reveal
  (`explorer /select,`), Delete (**`Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile` → RecycleBin**).
  Screenshot thumbnails via `BitmapImage` (`DecodePixelWidth=160`, `OnLoad`, frozen). `Load()` on
  `OnNavigatedTo`, `Items.Clear()` on `OnNavigatedFrom` (§3.9). Errors → `IErrorReporter.Warn`.
- **`LibraryView.xaml`**: header (title + Videos/Screenshots tab buttons w/ accent underline bound to
  ShowVideos/ShowScreenshots + Open folder/Refresh); `ItemsControl` of `Card` rows — 72×44 thumb (play glyph
  underneath, `Image` on top so a null thumbnail falls through to the glyph for videos), name+meta, Open/Show
  in folder/Delete. Item commands reach the VM via `RelativeSource AncestorType=ItemsControl` + `CommandParameter={Binding}`.
  Empty-state TextBlock overlays when `IsEmpty` (no inverse converter needed — list just renders empty behind it).
- Strings: Library_Videos/Screenshots/Open/Reveal/Delete/OpenFolder/Refresh/NoVideos/NoScreenshots (resx + Strings.cs).

### Verification (real GUI, UI-Automation driven)
- Nav→Library: Videos tab lists recordings (play badge, name, "639 KB · Today 09:52", Open/Show/Delete).
- Screenshots tab: real **thumbnails** of captured desktops, "2 MB · Yesterday 23:55".
- Delete: disk screenshots 2→1, list re-render shows the exact remaining item (CommandParameter binding good).
- Build clean, 54 tests, 0 warnings.

### Gotchas
- Nav RadioButtons fire `Command` on **Click**, not on programmatic `SelectionItemPattern.Select()` — UI-Automation
  tests must click the clickable point (mouse_event), not Select(), or the page won't actually navigate.
- `Microsoft.VisualBasic.FileIO` is available in the .NET Windows Desktop framework (no package ref needed) →
  gives Recycle-Bin delete for free.
- Screenshots folder is a **subfolder** of Recordings, so top-level `EnumerateFiles` on Recordings excludes it.

### Remaining for Phase 5
- Portable USB acceptance test (last item → 🏁 MVP 1.0-alpha).

---

## Session 2026-07-05 — Phase 5 (part 3): countdown overlay + recording toolbar

**Goal:** The visible recording UX — pre-roll countdown and a floating, capture-excluded recording toolbar.

### What was built
- **`CaptureExclusion`** (App/Services): static `Apply(Window, IOsCapabilities)` →
  `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE=0x11)` when `SupportsExcludeFromCapture` (Win10 2004+).
  Call after HWND exists (SourceInitialized).
- **`CountdownWindow`** (Views) + **`ICountdownController`/`CountdownController`**: borderless topmost overlay
  covering the target monitor (physical-pixel `SetWindowPos` like RegionSelectWindow), dim `#59000000`,
  centered 160px bubble with a `DispatcherTimer` ticking N→1 (scale+opacity `Pop()` each tick), Esc → cancel.
  `Run(monitor, seconds)` = modal `ShowDialog`, returns `DialogResult==true` (proceed) / false (cancel).
  Excluded from capture. Ctor has `excludeFromCapture=true` (test seam).
- **`RecordingToolbarWindow`** (Views) + **`RecordingToolbar`** service: acrylic bottom-centre bar (rec dot →
  amber when paused, mono elapsed, Pause/Resume, Screenshot, stats, Stop) bound to `RecordViewModel`;
  `ShowActivated=False` (no focus steal), excluded from capture, positioned via `SystemParameters.WorkArea`
  (DIP). Service observes `RecordViewModel.IsRecording` PropertyChanged → Show/Hide (covers every stop path).
  Attached in `App.OnStartup` (like tray). Ctor `excludeFromCapture=true` test seam.
- **`RecordViewModel`**: `ToggleRecord` → `StartRecording(withCountdown:true)`; new `StartRecordingFromCli()`
  (no countdown). `StartRecording` gates on `_countdown.Run(SelectedMonitor ?? primary, CountdownSeconds)`
  before `coordinator.Start`; cancel → `StartPreview()`. `App.ExecuteCliCommand` `--record` →
  `StartRecordingFromCli` (automation = start now).
- **Lifecycle fix (important):** app now sets `ShutdownMode = OnExplicitShutdown` early in `OnStartup`. Needed
  because transient overlay windows (and `--tray` with no shown window) would otherwise trip
  `OnLastWindowClose` — e.g. the toolbar closing on stop during `--tray --record` would quit the app. Main
  window close button → `Application.Current.Shutdown()` (ShellWindow.OnClose). Self-tests already Shutdown
  explicitly, so global OnExplicitShutdown is safe for them.
- Temp `--selftest-overlays` hook: captures countdown+toolbar via the WGC path with exclusion off (present)
  then toolbar with exclusion on (absent) → `overlays-visible.png` / `overlays-excluded.png`.

### Verification
- Countdown renders (bubble "8" + Esc hint). Toolbar renders (dot·00:00·Pause·Screenshot·Stop).
- **Exclusion proven**: cropped bottom-centre — toolbar present in `overlays-visible.png`, **absent** in
  `overlays-excluded.png` (`supportsExclude=True`). Same WGC path as recording.
- `--tray --record`→`--stop` survives toolbar open/close (procs stay 1, mp4 delta 1) under OnExplicitShutdown.
  Normal windowed launch alive w/ visible window. `--selftest-record` still 360 frames. 54 tests, 0 warnings.

### Gotchas
- Default `OnLastWindowClose` + a self-test/tray path with no persistent shown window → closing a transient
  window silently quits the process (exit 0, no result written). Root cause of a confusing "phase 2 never ran".
  Fixed via `OnExplicitShutdown`.
- `WDA_EXCLUDEFROMCAPTURE` also hides the window from GDI/BitBlt screenshots (by design) → to *see* an overlay
  render you must capture with exclusion off; hence the two-capture test seam.
- `CountdownWindow` uses `DialogResult` → must be shown via `ShowDialog` in production; the visual self-test
  shows it non-modally with a large `seconds` so it never ticks to 0 (which would throw on a non-dialog).

### Remaining for Phase 5
- Basic Library; portable USB acceptance test.

---

## Session 2026-07-05 — Phase 5 (part 2): CLI + single-instance forwarding

**Goal:** Make RecMode automatable (`--record/--stop/--screenshot/--tray`) and single-instance.

### What was built
- **`CommandLineOptions`** (App/Services): pure record parsing `--record`/`-r`, `--stop`/`-s`, `--screenshot`,
  `--tray`; unknown args ignored (forward-compatible); `HasAction` = record|stop|screenshot.
- **`SingleInstance`** (App/Services): named-mutex owner check (`Local\RecMode.SingleInstance.Mutex`, per-user;
  mutex intentionally never released — OS reclaims on exit, dodges same-thread-release). `TryForwardToPrimary`
  = `NamedPipeClientStream` → write `\n`-joined args (2s connect). `StartListening` = background
  `NamedPipeServerStream` accept loop → `Split('\n')` → `onArgs` callback; cancellable.
- **`App.OnStartup` wiring**: single-instance guard runs **before** host build — if not owner, forward args +
  `Shutdown(0)`. `--selftest-*` bypasses the guard (keeps the headless harness working). After shell resolve:
  `--tray` skips `shell.Show()` (headless; app kept alive by the tray HWND under OnLastWindowClose since no
  shown window ever closes). `ExecuteCliCommand(options, startup)` runs the action; `StartListening` marshals
  forwarded commands to the UI thread. Second launch without `--tray` → `ShowMainWindow()` (focus).
- **`ExecuteCliCommand`**: `record.EnsureDevicesLoaded()` (new public method = idempotent `LoadDevices`, no
  preview/metering) when `HasAction`, so `--tray --record` works before the view is shown. Screenshot →
  `TakeScreenshot`; record → guard `!coordinator.IsRecording` + `RecordCommand.CanExecute` then execute;
  stop → guard `coordinator.IsRecording` then toggle off (RecordCommand toggles).
- Disposal: `_singleInstance.Dispose()` in `OnExit`. CA1001 (App owns a self-authored disposable field) →
  justified `[SuppressMessage]` on `App` (WPF Application manages lifetime via OnExit; `_host` as `IHost`
  interface didn't trip it, my sealed class does).

### Verification (real GUI, e2e via PowerShell)
- Primary windowed, procCount=1. `RecMode --screenshot` → forwarder exit 0 in **231 ms**, new PNG appears,
  procCount stays **1** (dedup). Forwarded `--record` → `.recording.mkv` temp; forwarded `--stop` → new MP4
  (ffprobe: **h264 4096×1152 ~60 fps**). `--tray --record` → **MainWindowHandle=0** (no window), temp mkv
  present, survives forwarded `--stop` → MP4 delta=1, primary stays alive. 54 tests, 0 warnings.

### Gotchas
- `--tray` relies on OnLastWindowClose NOT firing when a window is never shown — confirmed the app stays alive
  via the tray HWND message pump. (If close-to-tray/X semantics change later, revisit ShutdownMode.)
- `RecordViewModel` lists (Monitors/Encoders) only populate on nav — CLI must call `EnsureDevicesLoaded` first.
- Self-test hooks intentionally kept (bypass single-instance) as the only headless recording verification;
  retire alongside a real test harness (Phase 10-ish).

### Remaining for Phase 5
- Countdown overlay; capture-excluded recording toolbar; basic Library; portable USB acceptance test.

---

## Session 2026-07-04 — Phase 5 (part 1): hotkeys, tray, screenshots, failure-mode UX

**Goal:** Start the MVP UX layer — global hotkeys, tray + minimize-to-tray, screenshots, and a real error-channel UI.

### What was built
- **`GlobalHotkeys`** (App/Services): message-only `HwndSource` (`HWND_MESSAGE`) + `RegisterHotKey`/`WM_HOTKEY`
  hook; `Register(mods, vk) → id`, `Pressed`/`RegistrationFailed` events; unregisters + disposes the source.
  `VirtualKeys` (F9/F10/F11). **`HotkeyBindings`** maps F9→RecordCommand, F10→PauseResumeCommand,
  F11→ScreenshotCommand; a failed registration → `errors.Warn("hotkey.in-use", …)`.
- **`ScreenshotCapturer`** (Capture): one-shot WGC grab — free-threaded frame pool, `TryGetNextFrame`, full-res
  BGRA staging readback honouring `target.Region` (clamped crop), `ManualResetEventSlim` 2s wait; returns
  `ScreenshotImage(W,H,Stride,Bgra)`. **`ScreenshotService`** (App): encodes PNG via `PngBitmapEncoder` to
  `settings.ScreenshotFolder ?? paths.ScreenshotsDirectory` using `FilenameBuilder` pattern + unique path,
  copies to clipboard (`Clipboard.SetImage`, swallow transient lock), raises `Captured`. Failures → `Warn`.
  `RecordViewModel.ScreenshotCommand`/`TakeScreenshot()` (enabled when a source exists); Screenshot button on
  the Record toolbar (new `Record_Screenshot` string).
- **`TrayIconService`** (App, H.NotifyIcon.Wpf 2.2.0): `TaskbarIcon` with a code-drawn 32px icon + context menu
  (Show / Start-stop / Screenshot / Quit); double-click shows; `window.StateChanged` → `Hide()` on Minimized
  (minimize-to-tray). Icon handle destroyed on dispose.
- **Failure-mode UX:** `ShellViewModel` subscribes to `IErrorReporter.ErrorReported` → snackbar
  (`SnackbarMessage/Visible/IsError` + `DismissSnackbarCommand`); warnings auto-dismiss (5s `DispatcherTimer`),
  blocking/fatal persist. Snackbar border added to `ShellWindow.xaml` (acrylic, accent/critical dot). Marshals
  to the UI thread via `Dispatcher.CheckAccess`.
- Wiring: 4 services registered in `Composition`; `App.OnStartup` resolves `HotkeyBindings.Register()` +
  `TrayIconService.Attach(shell)` on the UI thread after `shell.Show()`; added `--selftest-screenshot` hook
  (captures primary monitor synchronously on the STA thread → `selftest-result.txt`).

### Verification
- `--selftest-screenshot` → `success=True`, valid **5120×1440** PNG (2.1 MB) in `Recordings/Screenshots/`.
- Normal launch stays alive 4s with hotkeys + tray wired (DI + tray creation OK). Build clean, **54 tests pass**.

### Gotchas
- Assembly name is `RecMode.exe` (not `RecMode.App.exe`).
- Hand-written `Strings.cs` accessor (not designer-gen) → new resx keys need a matching property added there too.
- `Clipboard.SetImage` can throw if another app holds the clipboard; caught — the PNG is still saved.
- Killing the app leaves `Data/logs/crash/session.open` (the unclean-exit sentinel) — expected, not a crash.

### Remaining for Phase 5
- Countdown overlay; capture-excluded recording toolbar; real CLI (`--record/--screenshot/--stop/--tray`) +
  single-instance forwarding (replaces the self-test hooks); basic Library; portable USB acceptance test.

---

## Session 2026-07-04 — Phase 4 (part 1): audio mixer, meters, A/V mux

**Goal:** Get sound into recordings — system + mic mixer, meters, muxed audio, mixer UI.

### What was built
- **`RecMode.Audio`** (NAudio): `AudioLevel`, `AudioMath` (SoftClip=tanh, Peak, Rms — unit-tested),
  `MixSource` (one WASAPI capture → normalized to 48 kHz stereo f32 via `BufferedWaveProvider` +
  `MonoToStereoSampleProvider` + `WdlResamplingSampleProvider`; meter in DataAvailable; buffer bounded +
  DiscardOnBufferOverflow so metering-only doesn't grow), `AudioMixer`/`IAudioMixer` (system loopback +
  mic; per-source gain/mute; `PumpUntil(pipe, Func<TimeSpan> activeElapsed, token)` mixes with soft-clip
  and paces to **active elapsed** so audio pauses with video).
- **Encoding**: `FfmpegJob` gains `AudioPipeName`/`AudioCodec`/`AudioBitrateKbps`; `FfmpegArgsBuilder`
  adds the audio input + `BuildAudioArgs` (container steering: MP4/MOV→AAC, MKV→requested, WebM→Opus,
  FLAC valid on MKV); `FfmpegRecordingSession` creates + exposes the `AudioPipe`, closes both pipes on finalize.
- **Coordinator**: injects `Func<IAudioMixer>`; if `SystemAudioEnabled||MicrophoneEnabled`, sets the audio
  pipe on the job, starts a recording mixer, and (after the video pacer, to avoid the 2-input deadlock)
  runs an audio thread: `AudioPipe.WaitForConnection()` then `PumpUntil(..., () => _stateMachine.Elapsed)`.
  Audio torn down in Finalize/SafeTeardown.
- **UI**: RecordViewModel metering (separate meter mixer + `DispatcherTimer` ≤30 Hz → `SystemMeter`/`MicMeter`
  RMS; `SystemAudioEnabled`/`MicEnabled` toggles persisted; started/stopped with nav/minimize). RecordView
  Audio card (System + Mic toggle + `ProgressBar` meter); right panel wrapped in a `ScrollViewer`.

### Verification
- `--selftest-av` → MP4 with **h264 + aac (48 kHz, 2ch)**; video 5.999 / audio 6.000 s (~1 ms aligned).
- Audio card renders (screenshot): System toggle on + meter, Mic toggle off + meter. 54 tests, 0 warnings.

### Gotchas
- **ProgressBar `Value` binding defaults effectively TwoWay** on a read-only VM property → `InvalidOperationException`
  ("cannot work on read-only property"). Fix: `Value="{Binding SystemMeter, Mode=OneWay}"`.
- Meter mixer (VM) and recording mixer (coordinator) are **separate instances** (two WASAPI loopback clients
  — allowed) → clean ownership; recording mixer is fresh (no stale-buffer sync issue).
- Two-input pipe deadlock (again): start video pacing before waiting on the audio-pipe connection.

### Remaining for Phase 4
- Mic verified on real hardware (headless env = no mic signal, like AMF needed real AMD hw). Per-source
  mute/gain UI (mixer supports them). FLAC path. Caution meter colour >82%. Mid-recording mute/gain
  propagation to the recording mixer. ±40 ms soak sync test (beep+flash).

---

## Session 2026-07-04 — Phase 3 (part 1): pause/resume, safe recording, encoder fallback

**Goal:** Productionize the recording flow — pause/resume with no gap, safe recording, fallback chain.

### What was built
- **Pause/resume (PTS continuity, §3.7):** rewrote `PaceLoop` to be **Elapsed-driven** — target frame count =
  `_stateMachine.Elapsed.TotalSeconds · fps`; write until caught up, else sleep. Since `Elapsed` excludes
  paused spans and ffmpeg assigns PTS by frame index (`-r fps` rawvideo), pausing writes no frames → gapless,
  and resume produces no catch-up burst (Elapsed didn't advance during pause). `Coordinator.Pause/Resume`;
  `RecordViewModel` Pause/Resume button + `IsPaused`/`PauseButtonText`; StatsText shows "Paused".
  **Verified:** 3s + pause 2s + 3s → exactly 6.0s / 360 frames (not 8s).
- **Safe recording (§3):** when `SafeRecording && container==Mp4`, record to `<stem>.recording.mkv`, remux
  `-c copy -movflags +faststart` → MP4 on finalize, delete the mkv. On remux failure, keep the mkv + warn.
  **Verified:** `safe=true` in the log; final MP4 valid; no leftover mkv.
- **Encoder fallback chain (§3.6):** `BuildFallbackChain` (selected → same-codec other backend → any hw
  H.264 → libx264); `TryStartAnyEncoder` tries each, catching `EncoderStartException`. Added a **connection
  timeout** in `FfmpegRecordingSession.Start` (polls `_ffmpeg.HasExited` + 8s cap) so a bad encoder can't
  deadlock `WaitForConnection`. Coordinator now injects `IEncoderProbe`.
- **Disk-space pre-flight** (< 2 GB free → RecoverableWarning). **Richer stats:** `RecordingProgress` gains
  `Mbps` + `FileSizeBytes` (polled from the recording file); StatsText = "fps · Mbps · MB".

### Gotchas / notes
- Pause correctness hinges on ffmpeg assigning PTS by **frame index** (rawvideo `-r fps`), so only the frame
  **count** matters — wall-clock write timing is irrelevant to output timing. Pace by Elapsed·fps, done.
- Fallback handles **pre-connect** failures (ffmpeg exits/won't connect). **Post-connect** encoder-open death
  (e.g. nvenc "Cannot load nvcuda.dll" breaks the pipe after ~2 frames) is *prevented* by trial-encode gating
  (not offered) and otherwise handled by `HandleFatalPipeBreak` → Fatal + recoverable MKV. Live hw→sw
  Degraded swap mid-stream is still TODO (Phase 3 part 2).
- Added `--selftest-pause` hook (3s/pause2s/3s). Self-test dispatch now matches any `--selftest-<mode>`.

### Remaining for Phase 3
- Countdown wiring (state machine has the transitions; overlay lands Phase 5), full snapshot-tested args
  matrix, resource-control args (thread cap / sw+hw effort / process priority), auto-split (segment muxer,
  FAT32 4 GB), orphan-MKV detection + recovery on launch, mid-stream hw→sw Degraded fallback, gate re-check.

---

## Session 2026-07-04 — Phase 2 (region capture + overlay) + CPU budget

**Goal:** Region source with a select overlay + GPU crop; verify the Record-screen-open CPU budget.

### What was built
- **`RegionRect`** (readonly record struct) + `CaptureTarget.Region` / `CaptureKind.Region` / `FromRegion`.
- **GPU crop** via `VideoProcessorSetStreamSourceRect` (Vortice `RawRect`) in both `Nv12Converter`
  (recording) and `BgraScaler` (preview) — optional `sourceRect` param; the processor crops+scales in the
  same pass (no extra copy). `TryGetSourceSize` returns the region size; engines pass `target.Region`.
- **`RegionSelectWindow`** — full-monitor overlay: `WindowStyle=None`, `AllowsTransparency`, `Topmost`,
  dim `#66000000`; positioned in **physical pixels** via `SetWindowPos` (bypasses DIP math); selection in
  DIPs converted to monitor pixels with the composition transform `M11` (DPI-safe); rubber-band drag, live
  `W × H` px label, presets (1920×1080 / 1280×720 / Full), Esc=cancel / Enter=use, even-dim clamping.
- **`IRegionPicker`/`RegionPicker`** (shows the overlay modally; keeps the VM free of Views).
- **RecordViewModel**: `IsRegionSource` opens the picker on first switch (reverts to Screen on cancel);
  region persisted to settings (`RegionX/Y/Width/Height`) and restored on load; `ChangeRegionCommand`
  re-picks; `RegionLabel`/`ShowRegionInfo`; `CurrentTarget` returns `FromRegion`. RecordView: Region tile
  enabled, region-info panel with "Change…".

### Verification
- Overlay screenshot: dimmed screen + accent-bordered selection rect + px readout + preset/confirm toolbar.
- **Region crop correctness:** `--selftest-region` records a `RegionRect(100,100,1280,720)` → ffprobe
  confirms the output is exactly **1280×720** h264. Monitor-record regression still valid. 46 tests, 0 warnings.
- **CPU budget (§1):** Record-screen-open with live preview = **1.93% total CPU, 198 MB** (< 3% / < 250 MB).
  Note it's ~31% of one core — headroom for the allocation-profiling pass (preview readback + WritePixels +
  Dispatcher marshaling at 30 fps).

### Gotchas
- `SelectedMonitor` is a **property**, so `is not null` doesn't null-narrow it → bind a local
  (`is { } mon`) before passing to non-null params, else CS8604 becomes an error (WarningsAsErrors=nullable).
- Region select overlay must be positioned in physical pixels (SetWindowPos), then selection DIPs → pixels
  via `CompositionTarget.TransformToDevice.M11` — otherwise DPI scaling corrupts the region rect.
- `Vortice.RawRect(left, top, right, bottom)` for the source rect (not x/y/w/h).

### Remaining for Phase 2
- All-displays capture (WGC can't do the virtual desktop; needs DXGI Desktop Duplication for multi-monitor).
- HDR→SDR tone-map in the NV12 pass (needs an HDR monitor to verify; VideoProcessor color-space route).
- Allocation-free hot path (texture ring + pooled buffers, profiler-verified); preview CPU/core is the target.

### Note
- Build/commit here were briefly blocked by a command-safety classifier outage; resumed once it recovered.
- Added temporary `--selftest-region` hook alongside `--selftest-record` (both removed when Phase 5 CLI lands).

---

## Session 2026-07-04 — Phase 2 (part 1): live preview, window capture, cursor/border, black-frame watchdog

**Goal:** Productionize the capture engine — the headline being live preview in the Record screen.

### What was built
- **`CaptureTarget`** (Monitor|Window) + `WindowInfo`; `CaptureInterop`: `EnumerateWindows` (visible, titled,
  non-tool, non-DWM-cloaked), `CreateItemForWindow`, unified `CreateItem(target)`. Engines now take a target.
- **`BgraScaler`** — VideoProcessor BGRA→BGRA scaled + tight readback (single plane) for preview.
- **`IPreviewEngine`/`WgcPreviewEngine`** — separate WGC session, fits source into ≤1280×720, throttles to
  ~30 fps (early-out in FrameArrived), latest-frame pull (`TryGetLatestFrame`) + `FrameAvailable` signal.
- **`CaptureSessionConfig`** — cursor capture (Win10 2004+ projected property) + `IsBorderRequired=false`
  (Win11-only, SDK 22000+ → set via **reflection** since our TFM is 19041). Applied by both engines.
- **`RecordingCoordinator`**: `Start` now takes `CaptureTarget`; queries source size via
  `CaptureCapabilities.TryGetSourceSize`; passes `CaptureCursor`. **Black-frame watchdog** in the pace loop:
  samples 256 NV12 luma bytes every 16 frames; uniformly <20 for >3s → one RecoverableWarning.
- **RecordViewModel**: preview lifecycle (`StartPreview`/`StopPreview`/`RestartPreview`) tied to nav
  (`OnNavigatedTo/From`), minimize (`SetWindowMinimized`, called from `ShellWindow.StateChanged`), and record
  (stop preview on record start, resume on finish). `PreviewImage` (`WriteableBitmap`, `WritePixels` on the UI
  thread from the `FrameAvailable` signal). Window source: `IsScreenSource`/`IsWindowSource`, `Windows` list,
  `SelectedWindow`, `ShowWindowPicker`. **RecordView**: `Image` bound to `PreviewImage` (placeholder collapses
  via `HasPreview` trigger); Window tile enabled; display/window picker swap on `ShowWindowPicker`.

### Verification
- Screenshot: preview pane shows the **live 5120×1440 desktop**; Window tile enabled. Record self-test still
  produces a valid MP4 (h264_amf 4096×1152, 349 frames). 46 tests, 0 warnings.

### Gotchas
- `GraphicsCaptureSession.IsBorderRequired` doesn't exist in the 19041 projection (added SDK 22000) → set via
  reflection, guarded. `IsCursorCaptureEnabled` IS in 19041.
- Preview + recording use **separate** WGC sessions and are never simultaneous (preview stops on record start)
  — matches §3.9 "preview auto-pauses during recording."
- WriteableBitmap has thread affinity → create + `WritePixels` on the UI thread; engine only signals.

### Remaining for Phase 2 (not done)
- Region source + region-select overlay (dimmed layer, drag/resize, px label, presets, persisted, GPU crop).
- All-displays (virtual desktop) capture. HDR→SDR tone-map in the NV12 pass (needs HDR monitor to verify).
- Allocation-free hot path (texture ring + pooled buffers, profiler-verified). <3% CPU Record-open budget.

---

## Session 2026-07-04 — Phase 1: Minimal shell + Record essentials

**Goal:** First real recording through real UI. Productionize the Tier-1 spike; build the themed shell +
functional Record screen; pick monitor/encoder/format/fps/quality → Record → valid file.

### What was built
- **RecMode.Capture**: `MonitorInfo`, `CaptureInterop` (monitor enum via EnumDisplayMonitors, D3D device on
  discrete GPU, WGC item per-HMONITOR, surface→texture), `Nv12Converter` (VideoProcessor, ported from spike),
  `ICaptureEngine`/`WgcCaptureEngine` (publishes latest NV12 for the pacer; start/stop lifecycle §3.9),
  `CaptureCapabilities` (EnumerateMonitors/IsSupported).
- **RecMode.Encoding**: `EncoderInfo`+`EncoderCatalog` (codec×backend matrix), `IEncoderProbe`/`EncoderProbe`
  (`-encoders` list **+ trial-encode gating**), `FfmpegArgsBuilder` (CRF=51−q·0.38; amf cqp / nvenc cq / qsv
  global_quality / sw crf; +faststart for mp4), `FfmpegRecordingSession` (pipe+process, WriteFrame,
  StopAndFinalize, stderr capture, `EncoderPipeBrokenException`).
- **RecMode.App/Services**: `CaptureSizing` (hw-H.264 4096 cap + even dims), `RecordingProgress`,
  `RecordingCoordinator` (singleton; pre-flight → capture.Start → session.Start → state machine →
  CFR pacing thread writing NV12; broken pipe → Fatal + finalize; ≤4 Hz progress).
- **App UI**: `Themes/Palette.Light|Dark.xaml` (ported from `_ds/tokens/colors.css`), `Controls.xaml`
  (type ramp, Card, buttons, NavButton, SourceTile, retemplated ComboBox, Slider, ToggleSwitch, caption
  buttons), `ThemeManager` (swap palette + 5 accents + Changed event), `Interop/Windowing/WindowBackdrop`
  (Mica + dark titlebar). VMs (manual `SetProperty`/`RelayCommand`): Shell (nav + theme toggle),
  Record (devices/encoder/format/fps/quality/record), Settings (theme/accent/output), Library/Schedule stubs.
  Views: ShellWindow (WindowChrome, sidebar, DataTemplates), RecordView, SettingsView, Library/ScheduleView.
  `Resources/Strings.resx` + hand-written `Strings.cs` (ResourceManager wrapper — designer gen ran too late
  for WPF markup compile). DI in `Composition.cs`; theme applied in `App.OnStartup` before first paint.

### Verification
- **UI**: launched, screenshot-verified — dark theme, sidebar w/ accent pill, source tiles (Screen selected),
  combos show "Display 1 (5120 × 1440) · primary" / "H.264 · AMD AMF", `GPU · Amf`, `70 · CRF 24`, Record
  button + `00:00`. No startup crash, no errors in log.
- **Pipeline**: `--selftest-record` hook drives the *production* `RecordingCoordinator` 6s → valid MP4
  (h264_amf **4096×1152**@60, 354 frames, ffprobe-verified). Same coordinator the Record button calls.
- **Tests**: 46 pass (added `FilenameBuilderTests`, `FfmpegArgsBuilderTests`). Release build 0 warnings.

### Gotchas / learnings
- **`ffmpeg -encoders` is not a capability check** — it lists every compiled encoder. h264_nvenc showed
  "available" on this AMD box then failed opening ("Cannot load nvcuda.dll"), killing the pipe after 2
  frames. Fix: `EncoderProbe.TrialEncode` runs a 2-frame black-source encode per hardware candidate; only
  passers are offered. (This is the plan's §3.2 trial-encode, pulled forward.)
- **Hardware H.264 max width = 4096** → `CaptureSizing` scales hw-H.264 output down (5120→4096, height
  proportional, even dims). Software H.264/HEVC/AV1 keep native.
- WPF markup compile runs before MSBuild strongly-typed-resx generation → the generated `Strings` type isn't
  visible to XAML `x:Static`. Solution: hand-written `Strings.cs` over a `ResourceManager`.
- Custom ComboBox `ControlTemplate` ignores `DisplayMemberPath` for the selection box → override `ToString()`
  on `MonitorInfo`/`EncoderInfo` and drop `DisplayMemberPath`.
- Dev runs need ffmpeg next to the exe → `StageFfmpeg` MSBuild target copies `tools/ffmpeg` to `$(OutDir)ffmpeg`
  (SkipUnchangedFiles).

### Next actions
- Phase 2: live preview (D3DImage) in RecordView replacing the placeholder; window/region/all-displays
  capture; cursor toggle + Win11 border suppression; HDR→SDR tone-map; black-frame watchdog; allocation
  profiling. Remove the temporary `--selftest-record` hook when the Phase 5 CLI lands.

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
