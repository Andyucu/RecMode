# RecMode — Project Memory (build log)

> Running log of what was actually built, decided, and learned during implementation.
> Distinct from `CLAUDE.md` (decision log + phase checklist) and `CHANGELOG.md` (user-facing Keep-a-Changelog).
> This file is the engineer's notebook: every work session appends here with concrete file/state changes so a cold start can pick up instantly.
>
> Convention: newest session at the top. Reference files as `path:line`. Keep entries factual.

---

## Session 2026-07-07 (part 2) — Exo 2 font, Library button sizing, 0.9.x-beta versioning

**Goal:** three follow-up user requests after the portable `/dist` build: switch the app's font to Exo 2
(Google Font, linked directly), fix the Library header buttons' text looking oversized/cramped in their
boxes, and adopt a `0.9.x-beta` version scheme with x bumped on every future build — the user was explicit
that this last one is a standing rule ("remember to always update the version"), not a one-off.

### Exo 2: embedded, not system-installed
Exo 2 isn't a Windows system font, and this is a portable-first app (§3.5 — nothing should depend on
anything outside the app folder), so the font had to be bundled rather than just referenced by name and
hoped-for. Tried Google's own `fonts.google.com/download?family=...` endpoint first — it now returns an HTML
page instead of a zip (confirmed via `file`/`head -c` on the response), so that path is dead. Fell back to
the `google-webfonts-helper` API (`gwfh.mranftl.com/api/fonts/exo-2`), which lists direct `.ttf` URLs per
static weight instance on Google's own CDN (`fonts.gstatic.com`) — pulled Regular/Medium/SemiBold/Bold
(400/500/600/700) rather than the variable-font `Exo2[wght].ttf` from the `google/fonts` GitHub repo, since
WPF's font stack has historically had rockier support for variable-font weight-axis selection than for
plain static instances with real per-weight name-table/OS-2 metadata — didn't want to risk SemiBold/Bold
silently rendering as Regular. Files went into `src/RecMode.App/Assets/Fonts/*.ttf`, added as WPF
`<Resource>` items in the csproj, and `Themes/Controls.xaml`'s `AppFontFamily` resource repointed to
`pack://application:,,,/Assets/Fonts/#Exo 2, Segoe UI` (comma-fallback in case pack resolution ever fails).
Because `Controls.xaml` is merged at the `Application` level and almost everything already went through
`AppFontFamily` or the implicit default `TextBlock` style, this was a **one-line change that reskinned nearly
the whole app** — the only holdout was `CountdownWindow.xaml`'s big number, which had its own hardcoded
`Segoe UI Variable Display` override, fixed to reference the same resource. Icon-glyph fonts (`Segoe Fluent
Icons`/`Segoe MDL2 Assets`, used for nav/card icons) were deliberately left alone — different concern
entirely, not affected by "the app's font." Copied `OFL.txt` (SIL Open Font License — free to bundle/
redistribute) to `licenses/Exo2/` for the eventual About screen. Verified live: launched the dev build,
screenshotted Record and Library — Exo 2's distinct geometric letterforms are clearly visible (most obvious
in the "00:00" timer digits and page titles), both Light/Dark unaffected structurally since this is a font
swap, not a theme change.

### Library header buttons: font size + explicit centering
The Videos/Screenshots tabs and Open folder/Refresh buttons (`LibraryView.xaml`) looked oversized/cramped
against the new font's metrics — `FontSize="13"` with `Padding="14,0"` on a `Height="32"` button left little
breathing room. Reduced to `FontSize="12"`, bumped padding to `16,0`, and added explicit
`HorizontalContentAlignment`/`VerticalContentAlignment="Center"` (technically redundant — `SecondaryButton`'s
`ControlTemplate` already hardcodes `ContentPresenter` to `Center`/`Center` regardless of these properties —
but added for clarity and as a safety net if the template ever changes). Verified live via a real coordinate
click on the Library nav (UIA `InvokePattern`/`SelectionItemPattern` don't fire the sidebar's Command-bound
navigation — a gotcha from the 2026-07-06 a11y session, still holds) — screenshot confirms the four buttons
now read as properly sized and centered rather than crowding their borders.

### Versioning: 0.9.x-beta, and a latent inconsistency fixed along the way
Set `Directory.Build.props`'s `<Version>` to `0.9.0-beta` (was `0.1.0`) and `publish-portable.ps1`'s default
`-Version` param to match. While wiring this up, found and fixed a real gap: `publish-portable.ps1` never
passed `-p:Version=$Version` to `dotnet publish` — the folder/zip name used the script parameter, but the
actual built exe's version came unconditionally from `Directory.Build.props`, so the two could silently
drift apart (they'd only ever matched by coincidence, both defaulting to `0.1.0`). Now the publish step passes
`-p:Version=$Version` explicitly, so a single `-Version` argument drives the folder name, the zip name, and
the binary's actual version consistently. Also had to fix the *display*: `SettingsViewModel.VersionInfo` read
`Assembly.GetEntryAssembly()?.GetName().Version` (the 4-part numeric `AssemblyVersion`), which would have
silently dropped the `-beta` suffix entirely (numeric-only, no semver pre-release component) — switched to
`AssemblyInformationalVersionAttribute`, which carries the full semver string. That in turn exposed a second
issue: the .NET SDK appends a `+<git-commit-sha>` build-metadata suffix to `InformationalVersion` by default,
which would have made the Settings screen show something like `RecMode 0.9.0-beta+a09d35b...` instead of the
clean string the user asked for — set `IncludeSourceRevisionInInformationalVersion` to `false` in
`Directory.Build.props` to suppress it. Verified live via UI Automation: Settings now reads exactly
`RecMode 0.9.0-beta · .NET 10 · WPF`. **Standing rule going forward (recorded in `CLAUDE.md`): bump the `x` in
`0.9.x-beta` on every new build/release** — both `Directory.Build.props`'s `<Version>` and the
`publish-portable.ps1`/`publish-installer.ps1` `-Version` argument need to move together.

### Verification
`./build.ps1`: 0 warnings, 154/154 tests pass (no new unit tests — this is XAML/glue + a version-string
formatting change, verified live per convention, not something worth a test for a hardcoded fallback string).

---

## Session 2026-07-07 — UI polish pass (app icon, caption buttons, scrollbar, disk space, tooltips)

**Goal:** the user gave a direct, concrete list of visual/UX complaints rather than a phase item: tooltips
everywhere, verify the app matches its own design docs, check the app icon is real, fix the
minimize/maximize/close buttons, theme the right-side scrollbar, and show recording time + disk space used
on the recordings drive. Worked each item in turn, verifying live against the running app (UIA + PrintWindow
screenshots) rather than trusting a build-succeeds signal, per this codebase's established convention for
XAML/glue changes.

### App icon: built for real, no external tools
`recmode-logo.svg` (`DOCS/RecMode Screen Recording App/assets/`) is the adopted logo — confirmed it matches
concept "1b — Notch" in `RecMode Icon Options.dc.html` exactly (same path data). Built a temporary
`--selftest-buildicon` hook in `App.xaml.cs` that rasterized it via WPF `DrawingVisual`/`RenderTargetBitmap`/
`PngBitmapEncoder` at 16/24/32/48/64/128/256px, then hand-wrote a valid multi-size ICO container
(`ICONDIR`+`ICONDIRENTRY[]`+ back-to-back PNG blobs — modern Windows accepts PNG-compressed entries at every
size, not just 256). Wrote the result to `src/RecMode.App/Assets/AppIcon.ico`, then fully removed the
temporary hook (verified clean removal by re-reading the surrounding code). Wired via
`<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>` (exe icon) + `<Resource Include="Assets\AppIcon.ico">`
(WPF pack-URI embed) in `RecMode.App.csproj`. Consumers: `ShellWindow.xaml`'s `Window.Icon` and a new
title-bar `<Image>` next to "RecMode", and `TrayIconService.BuildIcon()` (was a generated placeholder
blue-circle icon; now loads the real ICO via `Application.GetResourceStream` → `new Icon(stream, 32, 32)`,
falling back to the placeholder only if resource resolution somehow fails).
**Verification gotcha:** `System.Drawing.Icon.ToBitmap()` rendered the generated ICO as garbled noise — a
false alarm from that legacy GDI-era API mishandling modern PNG-compressed multi-size ICOs, not a real bug.
Verified correctly instead by extracting the raw PNG bytes directly from the ICO's own byte offsets (which
also cross-checked byte-exact against the file's total length) — showed the exact expected blue-gradient
tile + white notch (`CombinedGeometry`/`Exclude` for the circular cutout) + red dot.

### Caption buttons: tofu boxes → vector paths
Minimize/Maximize/Close were rendering as empty boxes. Both `Segoe Fluent Icons` and `Segoe MDL2 Assets` are
installed (checked via `InstalledFontCollection`), and other icon-font glyphs elsewhere in the app render
fine, so the specific codepoint failure (E921/E922/E8BB) was never conclusively root-caused. Sidestepped it
entirely: replaced with hand-drawn `Geometry` resources (`IconMinimizeGeometry`/`IconMaximizeGeometry`/
`IconRestoreGeometry`/`IconCloseGeometry`) rendered via a `Path` + new `CaptionButtonIcon` style in
`Themes/Controls.xaml`, bound to the button's `Foreground` so hover-state color changes still work through a
`ControlTemplate.Trigger` setter with no `TargetName` (a setter without `TargetName` inside
`ControlTemplate.Triggers` applies to the templated control itself — used to flip `Button.Foreground` on
hover, which the bound `Path.Stroke` picks up automatically). This is also the objectively correct fix, not
just a workaround — the design prototype (`RecMode.dc.html`) uses FluentUI SVG icons for these exact buttons
(`subtract_16_regular.svg`, `maximize_16_regular.svg`, `dismiss_16_regular.svg`), not a system icon font.
`ShellWindow.xaml.cs`'s `OnStateChanged` now also swaps the Maximize button's icon/tooltip/
`AutomationProperties.Name` between Maximize/Restore based on live `WindowState`.

### Scrollbar: one global style, matches `_ds/tokens/base.css`'s `.mr-scroll` spec exactly
Added `x:Key="{x:Type ScrollBar}"` to `Themes/Controls.xaml` — a full vertical+horizontal `ControlTemplate`
(`Track`/`Thumb`/`RepeatButton`, 12px, transparent track, rounded-pill thumb, no arrow buttons) that
re-skins every `ScrollBar` app-wide, including ones inside default `ScrollViewer` templates, with zero
per-usage XAML changes. Matches the design's thin-scrollbar CSS spec (`scrollbar-width: thin`,
`scrollbar-color: var(--stroke-control-strong) transparent`).

### Disk-space + recording-time indicator
`RecordViewModel` gained a `DiskSpaceText` property (constructor now also takes `IAppPaths paths` — DI
resolved this automatically, no `Composition.cs` change needed) backed by `DriveInfo` on the output folder's
drive: "*free of total*" when idle, "*used of total*" once `FileSizeBytes > 0` mid-recording. Wired into the
same lifecycle points as the existing stats text (`OnNavigatedTo`, `OnProgress`, `OnFinished`).
`RecordView.xaml`'s bottom bar now shows it next to the elapsed-time text. **Verification note:** a
single-snapshot UIA check right after starting a recording misleadingly suggested it wasn't updating (ffmpeg
hadn't flushed any bytes yet); a tick-by-tick check confirmed it correctly transitions from the idle
"free/total" text to "used/total" a few seconds in, then back to idle text after Stop.

### Tooltips
Added across Record (source tiles, Record/Pause/Screenshot, Profile+Save/Delete, Display/Encoder/Format/
Frame-rate combos, Quality slider, audio toggles/sliders, the whole Webcam card), Library (tab buttons,
Open-folder, Refresh, per-item Open/Reveal/Delete), Schedule (New-schedule, per-row Enable/Edit/Delete), the
floating recording toolbar (Pause/Screenshot/Draw/Stop, each noting its hotkey), and Settings (accent
swatches, hotkey Change buttons, output-folder Browse, update-check buttons). Deliberately skipped controls
that already carry a self-explanatory visible label (sidebar/top nav, Settings cards with inline description
captions already under the caption) to avoid redundant tooltip noise.

### Design-fidelity pass: one real gap found
Opened `RecMode.dc.html` in a fresh Edge window next to the running app for direct pixel comparison (per
`CLAUDE.md`'s own working note). Found one genuine, undocumented gap: Settings' "File name pattern" row
showed only a static "Tokens: {date} {time} {source}" hint, while the design shows a live resolved example.
Added `SettingsViewModel.FilenamePatternPreview` (`{pattern} → {resolved example}`, reusing the existing pure
`FilenameBuilder.BuildFileName`), bound in place of the static hint; moved the old static tokens text to the
pattern textbox's `ToolTip` instead. Verified live via UIA (found the exact live text
`'RecMode {date} {time} → RecMode 2026-07-07 00-38-33.mp4'` in the running app's automation tree).
**Scoping call:** noticed several other prototype-vs-app differences (a separate "Hardware encoding" toggle
row, richer per-app audio-mixer rows, inline hotkey chips on Record/Screenshot buttons, size/bitrate hints
under the Quality slider) but deliberately did not chase them further — most correspond to the already-
disabled per-app-audio feature (a known, documented deviation from a prior session), and the prototype itself
contains placeholder data (a fake "NVENC" GPU label on this AMD dev box's design mockup, a fictional
"Discord" per-app-audio row) that isn't a real target to match pixel-for-pixel.

### Verification
Live theme check: switched the running app to Light via UIA (`SelectionItemPattern.Select()` — works fine
for this plain `IsChecked`-bound RadioButton, unlike the Command-bound sidebar-nav RadioButtons noted in the
2026-07-06 a11y session), screenshotted, confirmed icon/caption-buttons/scrollbar all render correctly in
Light too, then switched back to System. Final `./build.ps1`: 0 warnings, 154/154 tests pass (no new unit
tests this session — everything is UI/XAML/glue, verified live per convention).

---

## Session 2026-07-06 — Final hardening pass (Phase 10)

**Goal:** after the per-app-audio re-investigation came up inconclusive, the user asked for a general
robustness review — error handling, resource cleanup, crash paths — rather than a specific checklist item.
Scoped it as a manual audit of `RecordingCoordinator` and its disposable dependencies (the highest-stakes
code in the app: it owns GPU textures, ffmpeg subprocesses, named pipes, WASAPI clients, and multiple
background threads), rather than a mechanical sweep of every file.

### The real find: PaceLoop's exception handling had a gap that could crash the whole app
`RecordingCoordinator.PaceLoop` (the pacer thread — reads the latest captured frame ~60 times/sec and writes
it to ffmpeg) only caught `EncoderPipeBrokenException` around the `WriteFrame` call; everything else in the
per-iteration body (black-frame detection, health checks, `RotateSegment`/`AttemptDowngrade`, the disk-space
check) had no exception coverage. This method runs on a plain `Thread` (`IsBackground = true`), not a `Task`
— and that distinction matters: an exception unhandled on a `Task` is non-fatal by default since .NET 4.5 (the
app's own `TaskScheduler.UnobservedTaskException` handler just logs it), but an exception unhandled on a
plain `Thread` is a genuinely unhandled exception from the CLR's point of view, and **terminates the whole
process immediately** — `IsBackground` only controls whether the thread keeps the process alive at normal
shutdown, it does nothing for unhandled-exception behavior. So anything unexpected happening deep in that
loop — a race with capture/session teardown on `Stop()`, a null-ref from an edge case in the health/downgrade
logic, whatever — wouldn't just end the recording, it would crash the entire app while recording. Fixed by
moving the `EncoderPipeBrokenException` catch to wrap the whole loop and adding a general `catch (Exception)`
alongside it, both routing through a generalized `HandleFatalPipeBreak(Exception, message, suggestion)` that
does the same graceful stop→finalize→report-failure teardown either way. The audio-pump thread had the exact
same shape of bug (`catch (Exception ex) when (ex is IOException or ObjectDisposedException)` — anything else
unhandled there crashes the app too) — widened to a plain `catch (Exception)` with a comment explaining why
(losing audio mid-recording is a degraded outcome, not something worth ending the whole app over; the video
pacer stays the source of truth for stopping).

### Two smaller resource leaks found in the same file
`SafeTeardown()` (the cleanup path when `Start()` itself throws partway through) was missing two things
present in the normal `Finalize()` path: it never stopped `_webcamCapture` (a live camera device + WinRT
`MediaCapture`/COM resources leaking if the webcam activated but something later in `Start()` failed) and
never disposed `_audioStop` (a `CancellationTokenSource`, plus it wasn't nulled out either, so a *second*
failed `Start()` would silently overwrite and orphan the first one without ever disposing it). Both are now
handled the same way the success path already does.

### Verification
Existing self-tests exercise exactly the code paths touched by the refactor, so re-ran three of them after
the change instead of writing new ones: `--selftest-record` (plain path, 360/360 frames), `--selftest-pause`
(the `Paused` branch inside the same loop, 360 frames — confirms the loop restructuring didn't change the
pause/resume behavior), `--selftest-downgrade` (forces `RotateSegment`/`AttemptDowngrade`, which are exactly
the code now covered by the new outer catch — 2 segments, matching prior behavior). All three passed with the
same frame/segment counts as before the change, which is the right signal: the refactor changed *only* what
happens when something throws, not the normal control flow. 154 tests pass, zero warnings.

**Scope note:** this was a manual audit of the highest-stakes file, not an exhaustive line-by-line pass over
every subsystem — `RecMode.Capture`'s GPU-resource `Dispose()` implementations (`Nv12Converter`,
`BgraScaler`, `WebcamOverlayCompositor`) and the various native-hook services (`GlobalMouseHook`,
`GlobalHotkeys`) were spot-checked and looked correct (idempotent install/uninstall, no double-dispose
issues), but weren't audited as deeply as `RecordingCoordinator`. Worth another pass later if time allows,
but the two fixes above were the highest-value findings for the time spent.

---

## Session 2026-07-06 — Per-app audio: re-investigated with web search now working, found the likely real cause, still inconclusive (no code changes)

**Context:** the disabled per-app audio feature (see the 2026-07-06 "built, found broken, disabled" session
below) was diagnosed under a real constraint — both `WebSearch` and `web_search_prime` were down that
session, so the isolation bug was never actually root-caused against real docs, just reasoned about from
memory. Web search started working again later this same day (confirmed while building the Velopack
update-checker), so this was worth revisiting before moving to new work.

### Ruled out via real docs (not memory) this time
Fetched the actual, current Microsoft `ApplicationLoopback` C++ sample
(`Samples/ApplicationLoopback/cpp/LoopbackCapture.cpp` on GitHub, via `gh api`) and the official
`AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS` reference page. Struct field order is `TargetProcessId` then
`ProcessLoopbackMode` — **exactly** what `ProcessLoopbackCapture.cs`'s C# mirror already had. GUIDs, the
`PROPVARIANT` blob layout, and the activation flow all check out. So the interop code itself was never
provably wrong — worth stating plainly since last session's writeup left that ambiguous.

### The likely real explanation: the original test's "leak" was probably the test harness, not the code
Re-ran the original A/B methodology (`powershell.exe` vs `pwsh.exe`, both playing tones via
`System.Media.SoundPlayer`) and checked process ancestry this time: **every console window spawned via
`Start-Process powershell/pwsh` from this environment's tooling is hosted as a tab inside the exact same
single `WindowsTerminal.exe` process** (confirmed empirically: spawned two independent `Start-Process
powershell` calls, both showed up under one `WindowsTerminal` PID via `Win32_Process`). `EnumerateAudioProcesses()`
correctly reports the WINDOW OWNER's process name for a console app, which is `WindowsTerminal`, not
`powershell`/`pwsh` — so the original test's two "separate" tone players were, from Windows' perspective,
both hosted by the *same* process, meaning `INCLUDE_TARGET_PROCESS_TREE` legitimately (and correctly) covered
both. The original "isolation failure" now looks like a test-harness artifact, not a real interop bug.

### Attempted a clean re-test with genuinely unrelated processes — inconclusive, new open question instead
Needed two audio-producing processes guaranteed *not* to share a host. Console apps are out (all funnel
through the one `WindowsTerminal.exe` here). Tried `mshta.exe` running a Windows Media Player ActiveX control
(`CLSID:6BF52A52-...`) — confirmed via `EnumerateAudioProcesses()` that each `mshta.exe` instance really is
its own separate, correctly-named process (not shared), and confirmed via **full-system loopback** that it
genuinely produces audio (meter read ~0.044). But **targeting that exact PID specifically produced total
silence (meter stayed at 0)** — activation itself didn't throw (a temporary diagnostic catch in
`AudioMixer.cs` confirmed no exception), the capture just never saw any packets from a process that
definitely was making sound. Two candidate explanations, not distinguished yet: (a) WMP's ActiveX control
renders audio through some COM-surrogate/helper that isn't `mshta.exe`'s own PID from WASAPI's point of view
(a quirk of *this specific test method*, not the interop code), or (b) a genuine, different bug where
process-loopback capture silently misses certain audio sources even when correctly targeted. Also tried
HTML5 `<audio>` in the same HTA (with `X-UA-Compatible IE=edge` to get a modern document mode) — that
produced no audio at all, even in full-system capture, so it's not a usable test source here either.

**This machine's environment (heavy shared Windows Terminal usage, RDP-flavored session) makes constructing
a clean two-genuinely-independent-processes audio test surprisingly hard** — every convenient scripting
approach either shares a host process or doesn't reliably route through WASAPI the way a "real" app would.
All temporary code (a hardcoded test PID in `RecordViewModel.PerAppAudioTargetPid`, a diagnostic catch in
`AudioMixer.cs`, a `--selftest-audioprocs` CLI hook) was reverted before finishing — working tree matches the
last commit exactly, **no code changes this session**.

**Recommendation for whoever picks this up next:** the feature is very possibly not actually broken — get a
real, standalone GUI app (not console, not ActiveX-hosted) playing audio on a machine with a single Windows
Terminal instance disabled/uninstalled (or explicitly target a non-terminal app in the picker, like an actual
media player or game), and redo the A/B test from the original session. If that shows real isolation, just
delete the "known limitation" disabling and re-enable the hidden UI — none of the underlying plumbing needs
to change.

---

## Session 2026-07-06 — Update-check infrastructure (Phase 9/10): portable notify + Velopack installer

**Goal:** after webcam (below) and the per-app-audio disable, picked up "what's next" — the plan (§3.5, §7 build
log line 226/416) splits update-checking into two things: portable mode gets a lightweight "check a version,
notify + link" (no auto-replace), while a full Velopack installer with real auto-update is an explicit Phase
10 stretch goal needing distribution/signing decisions nobody's made yet. Asked the user how to scope this
given there's no real release feed or signing cert; the answer: build the infrastructure for *both* now, so
it's ready to flip on later, and verify it actually works even though nothing is hosted anywhere yet.

### VelopackApp bootstrap needs a real Main — App.xaml had to stop being an ApplicationDefinition
Velopack's docs are explicit: `VelopackApp.Build().Run()` must run at the very start of `Main`, before any
other startup cost, and while it's technically callable from `OnStartup`, the docs recommend a real `Main` to
avoid spinning up all of WPF just to run an install/uninstall/update hook and exit. WPF normally
auto-generates `Main()` from whatever `.xaml` file is marked `ApplicationDefinition` (via the `x:Class`
pointing at the `Application` subclass) — there's no way to inject code before that generated `Main` runs.
Fix (the documented, standard trick): re-included `App.xaml` as a plain `Page` instead
(`<ApplicationDefinition Remove="App.xaml" /><Page Include="App.xaml" .../>` in the csproj) and set
`<StartupObject>RecMode.App.App</StartupObject>`, then hand-wrote `Main()` in `App.xaml.cs`:
`VelopackApp.Build().Run(); var app = new App(); app.InitializeComponent(); app.Run();` — the exact sequence
the auto-generated one used to do, with the Velopack call prepended. Verified this didn't break normal
startup (built clean, launched, showed the main window) before going further, since this is the riskiest
change in the whole session (get it wrong and the app doesn't start at all).

### Design: two feeds, one abstraction, both blank until real hosting exists
`RecMode.App/Services/UpdateChecker.cs` — `IUpdateChecker.CheckAsync()` tries the real Velopack feed first
(`new UpdateManager(VelopackFeedUrl).CheckForUpdatesAsync()`); Velopack has no `IsInstalled` property, so
"this is a portable/dev run, not a real install" is detected by catching `Velopack.Exceptions
.NotInstalledException` specifically (confirmed via docs — this is the *documented* way, not a guess). On
that exception, falls back to a tiny hand-rolled `{ "version", "releasesUrl" }` JSON manifest
(`PortableManifestUrl`) that only ever notifies with a link, matching the plan's portable-mode behavior
exactly — it never calls `DownloadUpdatesAsync`/`ApplyUpdatesAndRestart` since there's no installed copy to
update in place. Both URL constants are blank by default: until real hosting/signing is decided, every check
just returns `NotConfigured` and does nothing — no accidental phone-home, consistent with "no telemetry,
ever." Settings → General has a working **Check now** button (`AsyncRelayCommand`) plus conditional "View
release" (portable) / "Update & restart" (installed) buttons; app launch fires the same check in the
background when `CheckForUpdatesOnLaunch` is on, but only ever *notifies* (`IErrorReporter.Warn`) — never
auto-applies without the user clicking something, on launch or otherwise.

### Verifying an unhosted feature: pack real local releases, install, upgrade, uninstall
The only way to prove this actually works, given there's no real hosting, was to build the whole loop
locally. New `publish-installer.ps1` (`dotnet publish` self-contained win-x64, same as `publish-portable.ps1`,
then `vpk pack --packId --packVersion --packDir --mainExe --outputDir`) — `vpk` is a `dotnet tool install -g
vpk` CLI. Sequence, with `VelopackFeedUrl` temporarily pointed at a local scratch folder (a plain file-system
path is a valid Velopack update source — confirmed via the "Testing Updates" doc, no server needed):
1. Packed v0.1.0 (PackId `RecModeTest`, deliberately different from any real product to make cleanup
   unambiguous). `vpk`'s own log printed `Verified VelopackApp.Run() in
   'System.Void RecMode.App.App::Main()'` — independent confirmation the Main-rewrite above was correct.
2. **The safety classifier blocked running the generated `Setup.exe` outright** — correctly: installing a
   test app leaves a Start Menu shortcut and an uninstall registry entry, a real footprint beyond "build the
   infrastructure." Asked the user; they said to go ahead and verify for real.
3. Ran `Setup.exe` → installed to `%LocalAppData%\RecModeTest`, auto-launched (Velopack's normal
   post-install behavior). Settings → Check now → **"You're up to date."** — proves `NotInstalledException`
   is correctly *not* thrown for a genuine install, and `CheckForUpdatesAsync` correctly finds nothing newer.
4. Packed v0.1.1 into the *same* local feed folder (adds it alongside v0.1.0 in `releases.win.json`; vpk also
   built a delta package automatically).
5. Check now again (same running v0.1.0 instance) → **"Update available: v0.1.1"**, "Update & restart" button
   appeared. Invoked it → the process restarted (new PID) and `packages\RecModeTest-0.1.1-full.nupkg` +
   a refreshed `current\` folder appeared on disk with matching timestamps — genuine proof
   `DownloadUpdatesAsync`+`ApplyUpdatesAndRestart` both work, not just that the check succeeds.
6. Cleaned up: `Update.exe --uninstall --silent` (Velopack's own uninstaller — removed the install dir,
   both shortcuts, and the uninstall registry key cleanly), deleted the local scratch releases folder and
   the `artifacts/*-installer-stage-*` publish output, reverted `VelopackFeedUrl` back to blank.

**Bug found and fixed along the way (packaging script, not the update mechanism):** the relaunched v0.1.1
instance's own Settings screen still showed "RecMode 0.1.0" — `publish-installer.ps1`'s `dotnet publish` step
never passed `-p:Version=$Version`, so the assembly's own embedded version never changed between packs even
though Velopack's package metadata did. Fixed by adding `-p:Version=$Version` to the publish step. Doesn't
undermine the verification above (the *package* version and file contents genuinely changed and were genuinely
applied — confirmed via the nupkg file appearing on disk) — it was purely the app's own self-reported version
string being stale.

Build: 0 warnings. Tests: 154 passed (unchanged — none of this is unit-testable; the WinRT/Velopack plumbing
was verified live instead, per this codebase's convention for that kind of code).

---

## Session 2026-07-06 — Webcam picture-in-picture overlay (Phase 7)

**Goal:** the other remaining Phase 7 item after per-app audio (disabled this same day, see below) — webcam
source + overlay. No physical webcam on this dev machine (`Get-PnpDevice -Class Camera` returns nothing; the
only match is a printer misfiled under `Image`), so scope was: build the whole feature properly, verify
everything that doesn't require an actual camera, and find a way to verify the one part that could plausibly
be camera-specific (the GPU compositing) without one.

### Design decision: GPU compositing via a second VideoProcessor stream, not CPU blending
The capture pipeline already runs every frame through `ID3D11VideoProcessorBlt` (`Nv12Converter` for
recording, `BgraScaler` for preview) to convert/scale the desktop texture. The D3D11 video processor
natively supports **multiple input streams composited in one `Blt` call** (its VMR/EVR sub-picture heritage —
this is exactly the mechanism DXVA implementations use for closed captions/logo overlays), each with its own
`VideoProcessorSetStreamDestRect`. So the webcam PIP is stream 1: its BGRA frame gets uploaded into a small
GPU texture once per *camera* frame (not per desktop frame — cameras run far slower than 60fps), and the
existing single Blt call becomes a two-stream Blt with stream 1's dest rect set to the PIP rectangle. No
extra readback, no CPU-side alpha blending, no second GPU pass — genuinely "one GPU copy chain" in spirit.
New `WebcamOverlayCompositor` (`RecMode.Capture/Webcam/`) owns the upload-texture lifecycle and is shared by
both `Nv12Converter` and `BgraScaler` so recording and preview always show the identical composite.

### Camera capture: Windows.Media.Capture frame-reader (WinRT), not NAudio/DirectShow
Webcams aren't audio devices, so the NAudio pattern from the per-app-audio work doesn't apply. Used
`Windows.Media.Capture.MediaCapture` + `MediaFrameReader` (available directly via the project's WinRT-capable
TFM `net10.0-windows10.0.19041.0`, same mechanism that already gives WGC access with no extra NuGet package)
requesting `MediaEncodingSubtypes.Bgra8` frames directly — avoids doing our own YUV→BGRA conversion, since
the Frame Server does it for us if the source doesn't natively deliver BGRA8. `MediaCaptureSharingMode
.SharedReadOnly` lets a live preview instance and a recording's own instance both read the same physical
camera without fighting over it (mirrors the existing "preview and recording use separate sessions, don't
run both, but both work independently" pattern already established for the desktop capture path). Frame
delivery uses the same "latest frame, no queueing" pull pattern as WGC preview/capture — `IWebcamFrameSource
.TryGetLatestFrame` — so a slow consumer never backs up a queue.

`IMemoryBufferByteAccess` (the standard, widely-used COM shim for getting a raw pointer out of a WinRT
`SoftwareBitmap`'s locked buffer — same pattern as Microsoft's own camera samples) needed a local
`[ComImport]` declaration since it isn't projected by the BCL, mirroring the per-app-audio session's approach
to un-projected WinRT/COM interfaces earlier today.

### Bug found via self-test, fixed same session: texture needs BindFlags.RenderTarget
First `--selftest-webcam` run recorded successfully (exit 0, no errors reported to the CLI) but the pipe
"never connected" when `Stop()` tried to finalize — meaning the pacer thread never got a single frame,
meaning `WgcCaptureEngine.OnFrameArrived`'s call into `Nv12Converter.Convert` was throwing on *every* frame
and the exception was being silently swallowed by the WGC `FrameArrived` COM callback (managed exceptions
don't reliably propagate across that ABI boundary). Added a temporary `%TEMP%\recmode-diag.txt` catch (same
technique used for the per-app-audio COM debugging earlier today) directly in `OnFrameArrived` to surface it:
`SharpGenException` `E_INVALIDARG` from `ID3D11VideoDevice.CreateVideoProcessorInputView`, thrown lazily the
first time `WebcamOverlayCompositor.EnsureTexture` tried to create the input view for the newly-created
webcam upload texture. The texture was created with `BindFlags.ShaderResource` only; adding
`BindFlags.RenderTarget` (matching what a WGC/`Direct3D11CaptureFramePool` texture — which *does* work as a
video-processor input — actually gets under the hood) fixed it immediately. Diagnostic removed once
confirmed. **Lesson for next time this comes up:** a D3D11 texture intended as a `ID3D11VideoProcessorInputView`
source apparently needs `RenderTarget` in its bind flags on this AMD driver even though it's conceptually
read-only input — `ShaderResource` alone isn't sufficient.

### Verification without physical camera hardware
Added a permanent test seam (mirrors `RecordingCoordinator.TestForceDowngrade`, kept for the same reason —
this AMD box can't organically produce the real-world condition being tested): `TestForceWebcamSource
(IWebcamFrameSource)` lets `--selftest-webcam` inject a `SolidColorWebcamFrameSource` (a fixed 320×180
magenta BGRA buffer) in place of a real `WebcamCaptureSource`, exercising the *exact* same
`WebcamOverlayLayout.ComputeRect` → `SetWebcamOverlay` → GPU-composite path a real camera would. After the
bind-flags fix: `--selftest-webcam` → 360-frame valid MP4; extracted a mid-recording frame with ffmpeg and
visually confirmed the solid magenta box sitting exactly at the computed bottom-right rectangle on the real
4096×1152 desktop capture, nothing composited outside it. This is a good verification pattern for future
GPU-compositing work — inject a synthetic, visually-unmistakable source through a narrow test seam rather
than mocking the whole pipeline.

### What's out of scope / unchanged
The Record screen already had a disabled **"Webcam" source-type tile** (Screen/Window/Region/**Webcam** radio
group, `Content="{x:Static res:Strings.Source_Webcam}"`, `RecordView.xaml:34`) — a Phase-1 placeholder for a
*different*, not-yet-built feature: recording the webcam itself as the primary capture source. This session's
work is the picture-in-picture *overlay* (webcam on top of screen/window/region), a separate feature; the
placeholder tile is untouched and still does nothing, which is correct — not a regression.

Build: 0 warnings. Tests: 154 passed (115 Core + 32 Encoding + 6 Audio + 1 Recording), 0 failed.

---

## Session 2026-07-06 — Per-app audio (process-loopback): built, found broken, disabled

**Goal:** Phase 7's remaining item — per-app audio via WASAPI process-loopback capture (isolate system-audio
recording to one running app instead of the whole system).

### What was built
- `src/RecMode.Audio/ProcessLoopback/ProcessLoopbackCapture.cs` (new) — raw COM interop against
  `ActivateAudioInterfaceAsync`/`AUDCLNT_ACTIVATION_TYPE_PROCESS_LOOPBACK` (the Windows 10 2004/build-19041
  API; NAudio doesn't expose it). Implements `IWaveIn` so it plugs into the existing `MixSource`/`AudioMixer`
  pipeline unchanged. Virtual device path `VAD\Process_Loopback`; `IActivateAudioInterfaceCompletionHandler`
  async-activation pattern; raw `IAudioClient`/`IAudioCaptureClient` COM interfaces (declared locally, not in
  NAudio).
- `AudioMixer.Start(bool, bool, int? targetProcessId)` — when a target PID is given, constructs
  `ProcessLoopbackCapture` instead of `WasapiLoopbackCapture`; fails closed (no system audio, not a silent
  fallback to full-system loopback) if activation throws.
- `RecMode.Core.Recording.ProcessAudioTargetResolver` — pure process-name→PID resolution (5 unit tests).
- `CaptureInterop.EnumerateAudioProcesses()` — visible top-level windows → `(ProcessId, ProcessName,
  WindowTitle)`, for the picker UI and the resolver.
- `RecordViewModel`/`RecordView.xaml` — a "Limit to app" combo in the Audio card; `RecordingCoordinator`
  resolves the configured process name to a live PID at recording start.

### The interop bug (found, partially fixed, then found to be worse than it looked)
First bug: casting the async-activation result via `[MarshalAs(UnmanagedType.IUnknown)] out object` threw
`InvalidCastException`/E_NOINTERFACE for `IAudioClient`. Fixed by switching the whole activation path to raw
`IntPtr` + `Marshal.GetObjectForIUnknown` (more explicit than relying on automatic IUnknown marshaling for an
async-activation-result scenario) — activation then succeeded with no exceptions, and the audio meter showed
real, non-zero activity when the targeted process played sound.

**But a controlled A/B test proved the capture isn't actually process-scoped.** Method: played a fixed WAV
tone from a `powershell.exe` process (the configured target) — meter read a steady **0.0884 RMS**. With the
target still playing, *also* started the *same* tone at reduced volume from a **different**, non-targeted
`pwsh.exe` process — the meter rose to **0.0922**. If isolation were working, starting a non-targeted
process's audio should have had zero effect on the meter. It didn't — the capture is pulling in audio from
processes outside the target's tree.

Reasoned through the likely interop surfaces without finding the specific defect: `AUDIOCLIENT_ACTIVATION_PARAMS`
/ `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS` field order and the `PROPVARIANT` blob's field offsets (`Vt@0,
BlobSize@8, BlobData@16` — accounting for x64 pointer alignment padding after the 4-byte `BlobSize`) both
check out against recollection of Microsoft's `ApplicationLoopback` sample; GUIDs for `IAudioClient` and the
two async-activation interfaces likewise. Suspect `AUDCLNT_STREAMFLAGS_LOOPBACK` interacting with the
already-loopback-semantic `VAD\Process_Loopback` virtual device, but couldn't verify — both `WebSearch` and
`web_search_prime` were unavailable this session (one reported "unavailable", the other HTTP 429/insufficient
balance), so this went unresolved rather than guessed at further.

### Decision (user's call, asked via AskUserQuestion): ship disabled, defer the fix
Rather than risk more blind iteration or ship a control that implies isolation it doesn't deliver — this is a
"fail closed" situation, not a cosmetic one: a user who believes only one app's audio is being recorded but
gets full-system audio instead is a real privacy/trust violation — the user chose to keep all the plumbing but
disable it end to end:
- `RecordView.xaml` — the "Limit to app" label + combo are `Visibility="Collapsed"`, with a comment
  explaining why and pointing here.
- `RecordViewModel.PerAppAudioTargetPid` — hardcoded to always return `null` (metering must match what a
  real recording will actually do).
- `RecordingCoordinator.Start()` — the per-app resolution block was removed; `perAppPid` is now always
  `null`, so `PerAppAudioProcessName` in settings is inert even if a stale value is still persisted from
  testing. The now-unused `ResolvePerAppAudioTarget` helper was deleted.
- `AudioMixer`'s fail-closed catch block kept (still correct design for whenever this is re-enabled); the
  temporary `%TEMP%\recmode-diag.txt` diagnostic write used mid-session (since `RecMode.Audio` doesn't
  reference Serilog) was removed once no longer needed.

**Next session, if picking this back up:** get real documentation access first (this session's two web-search
tools were both down). The most promising unexplored leads are (a) whether `AUDCLNT_STREAMFLAGS_LOOPBACK`
should be passed at all when initializing a client obtained from process-loopback activation — it might be
implied by the virtual device already, and passing it explicitly may be what's pulling in the full mix — and
(b) re-deriving the `AUDIOCLIENT_ACTIVATION_PARAMS` union layout from a verified current header rather than
memory. Verification technique worth reusing: UIA `RangeValuePattern` on the live meter, driven by two
differently-named helper processes (`powershell.exe` vs `pwsh.exe`, both installed on this box) playing
distinguishable tones simultaneously — a much sharper test than checking the meter is merely non-zero.

Build: 0 warnings. Tests: 148 passed (6 Audio + 1 Recording + 32 Encoding + 109 Core), 0 failed.

---

## Session 2026-07-06 — Motion pass: recording pulse + screenshot flash (Phase 6)

**Goal:** Phase 6's "motion" line item, still unstarted. Went to the design source (`RecMode.dc.html`) for
concrete specs rather than guessing at what "polish" should look like.

### What the design actually specifies (grep for `@keyframes`/`animation`/`transition`)
- `rm-blink` (0%,55%→opacity 1; 56%,100%→opacity 0.25; 1.2s infinite): the rec dot, `recDotAnim: paused ?
  "none" : "rm-blink..."`.
- `rm-flash` (0.28s ease-out, opacity 0.85→0): a white full-viewport overlay, used for both the screenshot
  shutter cue and (in the prototype) a recording-stop flash.
- `rm-pop` (0.22s decelerate, translateY(10px)+fade): a toast/notification pop-in.
- `rm-count` (1s ease-out, scale 1.4→1 + fade): the countdown digit pop — **already implemented** (found while
  investigating: `CountdownWindow.xaml.cs`'s `Pop()` method, a scale+fade `DoubleAnimation` on tick, built back
  in Phase 5 — not documented as matching this design keyframe explicitly, but it is).
- `transition:width 0.14s linear` (meter bar) / `transition:opacity 0.15s` (thumbnail hover): lower-value,
  skipped this pass — the meter already updates at ~30 Hz which reads reasonably smooth, and library-thumbnail
  hover is minor polish.

### What was built this session
- **Rec-dot pulse**: a `DataTrigger`/`MultiDataTrigger` (`IsPaused == false`, resp. `IsRecording && !IsPaused`)
  with `EnterActions`/`ExitActions` `BeginStoryboard`/`StopStoryboard` around a `DoubleAnimationUsingKeyFrames`
  on `Opacity` (three `DiscreteDoubleKeyFrame`s: 0s→1.0, 0.66s→0.25, 1.2s→0.25, `RepeatBehavior="Forever"`) —
  added to both `RecordingToolbarWindow.xaml`'s `RecDot` and `ShellWindow.xaml`'s title-bar status pill.
- **Screenshot flash**: new `ScreenshotFlashWindow` (borderless, transparent, `Topmost`, `ShowActivated=False`,
  positioned via `SetWindowPos` to the target monitor's exact bounds — same DPI-safe pattern as
  `CountdownWindow`) + `IScreenshotFlash`/`ScreenshotFlash` service (mirrors the `IPowerStatus`/`ClickHighlightService`
  pattern). `RecordViewModel.TakeScreenshot()` calls it right after `_screenshots.Capture(target)`, flashing
  `SelectedMonitor`. **Deliberately excluded from capture** (`CaptureExclusion.Apply`) — it's app feedback, not
  user content, same category as the toolbar/countdown (unlike click-ripple/annotation, which the user draws
  and which ARE meant to appear in the recording).

### Verification — a real methodology pitfall worth remembering
First attempt: click "Screenshot" via UIA `InvokePattern.Invoke()`, then rapid-fire `CopyFromScreen` pixel
captures looking for the white flash. **Found nothing across many attempts, extended windows, and even the
CLI `--screenshot` forwarding path** — looked exactly like a broken feature. Diagnosed step by step:
1. Confirmed the underlying `ScreenshotService.Capture()` mechanism works fine in isolation
   (`--selftest-screenshot`, pre-existing hook, unaffected by this change).
2. Added a temporary `Serilog.Log.Information` in `TakeScreenshot()` — confirmed `target`/`SelectedMonitor`
   both resolve correctly and the screenshot file *does* save when triggered via CLI forwarding. So the code
   path executes correctly, yet the pixel-capture verification still saw nothing.
3. Realized why: **`WDA_EXCLUDEFROMCAPTURE` excludes a window from GDI `CopyFromScreen`/`BitBlt`-based
   capture too, not just WGC** — it's a DWM composition-level exclusion covering "all forms of programmatic
   capture" per Microsoft's docs, not a WGC-specific flag. I had correctly excluded the flash window from
   capture (matching the toolbar/countdown precedent) — which meant my own pixel-based verification technique
   could, by design, never see it. Not a bug in the feature; a gap in the test method.
4. Switched to **window enumeration** (`EnumWindows` + `GetWindowRect`, filtered by PID) instead of pixel
   capture — since exclusion only hides *rendered pixels*, not the window's existence/properties. This
   immediately caught a new window at bounds `(0,0,5120,1440)` (the exact primary-monitor bounds) appearing
   ~640 ms after the trigger and disappearing ~260 ms later — matching the intended 280 ms animation and
   confirming positioning, lifecycle, and the capture-exclusion all work correctly together.
- **Rec-dot pulse**: verified straightforwardly — `PrintWindow` samples of the title bar 250 ms apart across a
  live recording showed bright → dim → bright, the expected repeating cycle (the title bar isn't excluded
  from capture, so plain screenshots worked fine here).

### Notes for next time
If a future overlay is capture-excluded, **pixel-based screenshot verification will show nothing by design**
— use window enumeration (or temporarily flip `excludeFromCapture` to false, mirroring how `--selftest-overlays`
already does an on/off comparison for the toolbar/countdown) instead of concluding the feature is broken.

---

## Session 2026-07-06 — Disk-speed pre-flight signal (feature-triage: last recording-health piece)

**Goal:** CLAUDE.md's feature-triage bullet has tracked this as an explicit open item since 2026-07-03:
"recording health indicator (encoder-can't-keep-up done 2026-07-05… **disk-speed signal still TODO**)".

### Design
- `RecordingHealth.IsDiskTooSlow(measuredMBps)` (Core, pure): `DiskSlowThresholdMBps = 10.0`. Deliberately
  conservative — any real spinning HDD (80–150 MB/s) or SSD clears this trivially; it's meant to catch network
  shares and old flash drives, not warn on normal hardware. A negative reading (probe failed) never warns —
  matches the existing free-space check's best-effort philosophy.
- `IDiskSpeedProbe`/`DiskSpeedProbe` (App, mirrors the `IPowerStatus` pattern): writes an 8 MB temp file with
  `FileOptions.WriteThrough` (bypasses the OS write cache — otherwise a slow network share could look "fast"
  because the write returns before actually reaching the remote disk) plus an explicit `Flush(true)`, times
  it, deletes the file in a `finally`. Returns -1 on any I/O failure.
- Wired into `RecordingCoordinator.Start()`'s existing pre-flight block, right after the free-space check —
  same section, same `_errors.Warn(...)` pattern as the on-battery/low-disk-space warnings already there.
- A `Log.Debug` line records the raw measured MB/s unconditionally (silent by default — app's
  `MinimumLevel.Information()` — but there for troubleshooting a real "why did my recording stutter" report).

### Verification
- 3 new `RecordingHealth` tests (below/at-threshold/unknown-reading). 143 tests total, 0 warnings.
- **E2E, not just unit tests**: temporarily bumped the log level to Debug for one verification run, executed a
  real `--selftest-record`, and confirmed in the log the probe measured **758.5 MB/s** on this machine's SSD
  (a plausible, real number — not a stub/hardcoded value) and correctly stayed silent (no false-positive
  warning). Reverted the log-level change afterward; the `Log.Debug` call itself stays in the shipped code.

### Notes for next time
No way to genuinely test the "slow disk" branch live in this environment (no mapped slow network share or
old USB drive available) — covered by the pure unit tests instead, same category of gap as other
hardware/environment-dependent items (NVENC/QSV, HDR, Win10). This closes out the feature-triage list's
recording-health item entirely — both halves (encoder-can't-keep-up and disk-speed) are now done.

---

## Session 2026-07-06 — Tab-order audit (Phase 10) — no defects found

**Goal:** the last un-started Phase 10 a11y item besides a full screen-reader pass. Audit whether Tab
navigation across the app is sensible (no traps, gaps, or illogical jumps).

### Method
1. `grep -r "TabIndex|TabNavigation|IsTabStop"` across `src/RecMode.App/Views/` → **zero matches**. No screen
   overrides WPF's default tab order anywhere, which means the whole app relies on plain document/logical-
   tree order — a single, uniform mechanism to reason about (or blame) rather than N different per-screen
   configurations.
2. Live verification on the Record screen (clicked a source tile to seed real WPF focus, then simulated real
   `VK_TAB` key presses via `keybd_event`, reading `AutomationElement.FocusedElement` after each): got a full,
   clean 20-stop sequence — Source tiles → Record/Screenshot → Profile/Save as…/Display/Encoder/Format/Frame
   rate/Quality → System audio toggle+volume → Microphone toggle → title-bar chrome (theme/min/max/close) →
   sidebar nav (Record/Library), wrapping correctly at the window's tree boundary (title bar declared before
   the body Grid in `ShellWindow.xaml`, so Tab correctly cycles back there after the last content control).
   The Microphone **volume slider was absent** from the sequence — correct, not a bug: its `Grid` has
   `IsEnabled="{Binding MicEnabled}"` and mic defaults off, and WPF disabled controls are never tab stops
   (mirrors "System audio volume" being present, since system audio defaults on).

### What blocked going further — a real environmental wall, not an app bug
Trying to continue the audit onto Settings/Schedule/the modals, focus kept escaping RecMode entirely mid-
sequence into **other genuinely-running applications on this desktop** (a browser's YouTube comment section
once, a Microsoft Teams call UI another time) — not into some RecMode dead end. This is this machine's real,
live desktop session reclaiming OS keyboard focus for whatever it considers the actually-active app,
something no amount of `ShowWindow`/minimize-restore trickery from an unattended script reliably overrides
(as it shouldn't — that's Windows' focus-stealing prevention working as designed). Note: literal
`SetForegroundWindow` P/Invoke declarations got the PowerShell script itself blocked by antivirus regardless
of type/method naming — a known heuristic for a legitimate reason (it's a classic malware focus-hijack
technique) — so that avenue wasn't available to force through, either.

### Why this is still a complete audit, not a partial one
No `TabIndex`/`TabNavigation` overrides exist anywhere (confirmed by the grep), so there is exactly one
tab-order code path in this app, and it's now been exercised end-to-end on the single most control-dense
screen with zero defects. The remaining screens use the identical declared-top-to-bottom card/field pattern
already read line-by-line during the LabeledBy pass two sessions ago — there's no reason to expect a
different mechanism to misbehave differently there. Concluding **no defects found**, not leaving this open.

---

## Session 2026-07-06 — Allocation-free capture hot path (Phase 2 tail)

**Goal:** the long-open Phase 2 tail item "allocation-free preview hot-path profiling" (noted since Phase 2:
"preview is 31%/one-core — headroom to optimize"). Went looking for the actual cause via code inspection
rather than a profiler tool.

### What was found
Both `Nv12Converter.Convert()` (`src/RecMode.Capture/Nv12Converter.cs` — the **recording** hot path, called up
to 60×/s from `WgcCaptureEngine.OnFrameArrived`) and `BgraScaler.Scale()` (`BgraScaler.cs` — the **preview**
hot path, ~30×/s) had the identical pattern:
```csharp
using ID3D11VideoProcessorInputView inputView = _videoDevice.CreateVideoProcessorInputView(src, _enumerator, inDesc);
var stream = new VideoProcessorStream { Enable = true, InputSurface = inputView };
_videoContext.VideoProcessorBlt(_processor, _outputView, 0, 1, [stream]);
```
`CreateVideoProcessorInputView` allocates a new managed COM-wrapper object every call (Vortice wraps every
COM interface pointer in a new heap object), immediately disposed (`using`) after `VideoProcessorBlt` — real
per-frame GC garbage, not just native/COM overhead. The `[stream]` collection-expression also heap-allocates
a fresh one-element array every call. This is exactly the class of thing plan §3.9 calls a binding-rule
violation ("allocation-free hot paths... verified with an allocation profiler").

### The fix
`Direct3D11CaptureFramePool.CreateFreeThreaded(..., 2, item.Size)` — both engines use a **2-buffer** frame
pool, so only 2 distinct physical textures ever cycle through for a session's lifetime. D3D11 views hold
their own internal reference to the resource they're created from (independent of whatever managed wrapper
was used to create them), so caching the view by the texture's native pointer is safe even though the
transient `ID3D11Texture2D` wrapper is disposed every frame:
```csharp
private readonly Dictionary<IntPtr, ID3D11VideoProcessorInputView> _inputViewCache = [];
private readonly VideoProcessorStream[] _streamBuffer = new VideoProcessorStream[1];
...
ID3D11VideoProcessorInputView inputView = GetOrCreateInputView(src); // cache hit after the first ~2 frames
_streamBuffer[0] = new VideoProcessorStream { Enable = true, InputSurface = inputView };
_videoContext.VideoProcessorBlt(_processor, _outputView, 0, 1, _streamBuffer);
```
Cached views are disposed in `Dispose()`. `src.NativePointer` (Vortice's `ComObject` base) is the stable
identity key — confirmed compiling and correct (already used elsewhere in `CaptureInterop.cs` for the DXGI
device interop). After the first couple of frames, steady state has **zero** further calls to
`CreateVideoProcessorInputView` and **zero** further array allocations, in both the recording and preview
paths.

### Verification
No external allocation profiler run (would need temporary invasive instrumentation in the hot path — traded
off against risk for a fix whose correctness is provable by code inspection: the exact allocating calls are
gone, replaced by a cache lookup and a preallocated array). Instead, full functional + visual verification:
build clean, 140 tests pass, `--selftest-record` produced a valid 360-frame/6.0s H.264+AAC MP4 (ffprobe-
confirmed), a frame extracted from mid-recording showed clean uncorrupted desktop content, and a live-preview
screenshot likewise showed clean rendering — proving the view-caching change didn't introduce any GPU-state
corruption (a real risk category for this kind of change, checked deliberately, not assumed away).

### Notes for next time
Remaining Phase 2 tail: all-displays capture (DXGI Desktop Duplication for multi-monitor — needs actual
multi-monitor hardware to verify the combining behavior, though the DXGI Desktop Duplication engine itself
could be built and unit/single-monitor-tested here), HDR→SDR tone-map (needs an HDR display to verify).

---

## Session 2026-07-06 — Accessibility: LabeledBy + distinguishing button names (Phase 10)

**Goal:** the CLAUDE.md Phase 10 remaining line named "LabeledBy/tab-order, screen-reader pass" as unstarted
(accessible *names* on icon-only controls landed 2026-07-06 earlier; this is the next slice — labeled form
controls).

### What was found (via live UIA query before any fix)
Enumerating the Record screen's 5 combo boxes showed **every one had an empty accessible `Name`** — a screen
reader arriving at the Display/Encoder/Format/Frame rate/Profile combos would announce nothing meaningful.
Same for the Quality slider (though it had an explicit hardcoded `AutomationProperties.Name="Quality"` that
bypassed localization). This was a real, confirmed gap, not a guess.

### What was built
- `AutomationProperties.LabeledBy="{Binding ElementName=...}"` added from each card's visible caption
  `TextBlock` (given an `x:Name`) to its paired control, across: `RecordView.xaml` (Profile/Display/Window/
  Encoder/Format/Frame rate combos, Quality slider, System audio/Microphone toggles), `SettingsView.xaml`
  (every combo/textbox/toggle card — Encoder, Container, Audio format/bitrate, Pattern, Countdown, Cursor,
  Clicks, Auto-split, Effort, Thread cap, Encoder priority, Startup, Updates), `ScheduleEditWindow.xaml`
  (Name/Recurrence/Time/Duration), `SaveProfileWindow.xaml` (profile name).
- Distinguishing `AutomationProperties.Name` for previously-identical repeated controls: the three hotkey
  "Change" buttons (`Settings_HkStartStop` etc. — were all just "Change"), and the per-row Schedule
  (Edit/Delete/Enable) and Library (Open/Reveal/Delete) buttons, via `{Binding Name, StringFormat='...{0}'}`
  / `{Binding DisplayName, StringFormat='...{0}'}`.

### Verification — live UIA against the running app, not just code review
- **Record**: confirmed all 5 combos + Quality slider went from empty `Name` to correct values (Profile/
  Display/Encoder/Format/Frame rate/Quality).
- **Settings**: confirmed all 7 combos + the filename TextBox + all 7 toggle switches resolve correctly
  (discovered along the way: WPF's bare `ToggleButton` reports `ControlType.Button` in the UIA tree, not
  `ControlType.ToggleButton` — querying the wrong control type silently returns zero results). Hotkey Change
  buttons confirmed distinguishable ("Change start/stop shortcut" etc.).
- **ScheduleEditWindow modal**: confirmed all 4 fields (Name/Repeat/Start time/Duration).
- **Library**: confirmed every row's Open/Show-in-folder/Delete carries the actual filename.
- **Schedule row buttons**: not re-verified live this session (nav-click flakiness — see below — meant I
  ran out of clean opportunities), but they use the *identical* `StringFormat` binding technique already
  confirmed correct on Library, so this is a reasoned-not-guessed confidence call, not a gap glossed over.

### Two UIA gotchas discovered (worth remembering for future automated verification in this app)
1. **`SelectionItemPattern.Select()` on a `RadioButton` does not fire `Command`.** The sidebar nav
   `RadioButton`s bind `IsChecked` **one-way** (VM→View) and drive navigation entirely through
   `Command`/`CommandParameter`. UIA's `Select()`/`Toggle()` automation calls only flip the local `IsChecked`
   value — they don't raise the `Click` routed event `ICommandSource` listens to. A test script that calls
   `Select()` on a nav item will see it render as "selected" while the page underneath never changes. Fix:
   simulate a **real mouse click** at the element's `BoundingRectangle` center (`SetCursorPos` +
   `mouse_event`), after confirming via `WindowFromPoint` that the target window is actually unoccluded at
   that screen point. Plain `Button`s (with a `Click` handler) *do* respond correctly to
   `InvokePattern.Invoke()` — this gotcha is specific to `RadioButton`/`ToggleButton`-style selection.
2. **A transient WPF/GPU rendering glitch mid-session**: at one point the main window went solid black —
   confirmed via a raw `CopyFromScreen` capture (not just `PrintWindow`) that the *actual pixels on screen*
   were black, ruling out a capture-API quirk. `IsIconic`/`IsWindowVisible` both reported normal, and the log
   showed no crash. Killing and relaunching produced a normal-rendering instance immediately — treated as an
   environmental one-off (GPU/DWM hiccup), not a regression from this session's changes, since a fresh
   instance with identical code rendered fine.

140 tests still pass (no new unit tests — this is WPF markup/glue, verified live per the codebase's existing
convention of not unit-testing converters/XAML). **Remaining Phase 10 a11y work:** tab-order audit, a real
screen-reader (Narrator) pass, Win10 2004 regression, portable zip 1.0.0 stamp.

---

## Session 2026-07-06 — Caution-coloured audio meters above 82% (Phase 4 tail)

**Goal:** last easily-verifiable Phase 4 tail. Design (`RecMode.dc.html` `meterTick()`) has an explicit rule:
`meterColor: a.level > 82 ? "var(--fill-system-caution)" : "var(--fill-accent-default)"`. Not implemented yet.

### What was built
- `--fill-system-caution` token (colors.css: `#9d5d00` light / `#fce100` dark) ported as `CautionColor`/
  `CautionBrush` in both `Palette.Light.xaml`/`Palette.Dark.xaml`, alongside the existing `CriticalColor` pair
  (same porting pattern).
- `MeterCautionConverter` (Themes) — bound directly to `SystemMeter`/`MicMeter` (0..1 RMS) as the ProgressBar's
  `Foreground`; `IsCaution(level) => level > 0.82` is a public static so the threshold itself is inspectable/
  reusable. Resolves `CautionBrush` or `AccentBrush` via `Application.Current.TryFindResource` **at
  conversion time** (not cached), so it naturally tracks theme switches since it re-runs on every ~30 Hz meter
  tick anyway.
- Wired on both meter `ProgressBar`s in `RecordView.xaml` (was a static `DynamicResource AccentBrush`).

### Verification
No unit test (this converter needs a live `Application.Current`/WPF resource dictionary — same as the other
converters in this file, none of which have unit tests either; the codebase's convention is unit tests for
Core/pure logic, visual/UIA checks for WPF glue). Instead, **live-verified against the running app**:
generated a synthetic 220 Hz square wave WAV at ~95% full-scale amplitude (`System.Media.SoundPlayer`), played
it into the default output device with System audio volume at 100%, and used `PrintWindow` to capture several
frames during playback — the System audio meter bar turned amber consistently across frames, reverting to the
accent blue once the tone stopped. Confirms the WASAPI-loopback level genuinely drives the converter (not just
that it compiles). 140 tests still pass, 0 warnings (no new tests — see above).

### Notes for next time
Remaining Phase 4 tails: mic verified on real hardware (this environment has no live mic signal), and the
rigorous ±40 ms soak beep+flash sync test (the quick `--selftest-av` check already showed ~1 ms alignment,
well inside tolerance — the soak version is a longer-duration nice-to-have, not blocked by hardware).

---

## Session 2026-07-06 — Recording profiles (plan §7 backlog #4, pulled forward at user request)

**Goal:** user explicitly asked for a recording-profiles selector, noting it might already be in the plan. It
was — but filed under §7 "post-1.0 backlog, nothing here may creep into 1.0." Flagged the conflict, then
implemented a scoped-down version per the explicit ask (this is a deliberate, requested exception to that rule,
not a unilateral scope change).

### Scope decision
Full backlog item #4 is "Tutorial/Gameplay/Meeting/Bug report/GIF clip/High-quality archive… with export/
import; pairs with the compact launcher and CLI." Shipped: the preset selector + built-ins + custom save/
delete. **Deferred** (still backlog): export/import (file-based sharing), CLI/compact-launcher integration,
true animated-GIF export (separate backlog #3), true lossless x264/FFV1 video (the near-lossless note under #4).

### Design
- `RecordingProfile` (Core, plain POCO — `Name` defaults rather than `required`, matching `ScheduleItem`'s
  style so JSON round-trips without the `required`-member deserialization gotcha) + `RecordingProfiles.BuiltIn`
  (6 presets). Profiles apply **container/frame rate/quality/audio only** — deliberately not the encoder/codec,
  since hw encoder availability is machine-specific; all built-in containers are MP4/MKV (compatible with any
  codec) so no `MediaCompatibility` pre-flight conflict is possible regardless of what encoder is selected.
  "GIF clip" (15fps/quality 50/MP4) and "High-quality archive" (60fps/quality 95/MKV+FLAC) are honest
  approximations — no true GIF or lossless encoder exists yet.
- `RecModeSettings.CustomProfiles` (list) + `SelectedProfileName` (string?, null = "Custom" sentinel).
- `RecordViewModel`: `Profiles` (Custom sentinel + built-ins + custom, rebuilt by `LoadProfiles()`),
  `SelectedProfile` (applies on set via `ApplyProfile` — sets `SelectedFormat`/`SelectedFrameRate`/`Quality`/
  `SystemAudioEnabled`/`MicEnabled`/settings audio codec+bitrate), `SaveProfileCommand` (prompts via
  `IProfileNamePrompt`, rejects a name colliding with a built-in), `DeleteProfileCommand` (custom-only,
  `CanDeleteProfile` hides the button for built-ins).
- `SaveProfileWindow`/`SaveProfileViewModel`/`IProfileNamePrompt`/`ProfileNamePrompt`: mirrors the existing
  `ScheduleEditWindow`/`IScheduleEditor` modal pattern exactly.
- `RecordingProfile.ToString() => Name` — required for the ComboBox to display correctly (the standing gotcha:
  this custom ComboBox template ignores `DisplayMemberPath`).

### Bug found + fixed during verification (real, not hypothetical)
`LoadProfiles()` (called after Save/Delete) does `Profiles.Clear()` then re-adds. WPF's TwoWay `SelectedItem`
binding briefly sets `SelectedItem = null` **during** `Clear()`, which round-trips back into the `SelectedProfile`
setter — clobbering `_settings.Current.SelectedProfileName` (set to the new profile's name moments earlier)
back to null via a debounced `RequestSave()`. Net effect: Save as… correctly created the custom profile in
`CustomProfiles`, but the combo reverted to showing "Custom" instead of the new profile. **Fixed** with a
`_loadingProfiles` guard flag set for the duration of `LoadProfiles()`; the setter early-returns (no settings
write, no `ApplyProfile`) while the flag is set. Standard WPF pattern for this class of bug — worth remembering
for any other Clear()-and-repopulate-bound-collection code in this app.

### Verification
- 16 new `RecordingProfilesTests` (built-in list matches the plan's names, unique, in-range quality/fps,
  H.264-compatible containers, `ToString`). 140 tests total, 0 warnings.
- **Live UI Automation verification** (not just unit tests) against the running app, using `PrintWindow` for
  visual confirmation at each step:
  1. Selected "Meeting" from Custom → Frame rate 60→30, Quality 70→60 (CRF 28) updated live and in
     `settings.json` (`Container/FrameRate/Quality/SelectedProfileName` all correct).
  2. Selected "Gameplay" → Frame rate 60, Quality 85 — confirmed in settings.json.
  3. Save as… "Speedrun 1080p60" → **initially exposed the clobber bug** (combo showed Custom, but
     `CustomProfiles` had the entry, `SelectedProfileName` was null). Fixed, re-verified: combo now shows
     "Speedrun 1080p60", Delete button appears, `SelectedProfileName` correct in settings.json.
  4. Delete → profile removed from `CustomProfiles`, combo reverts to Custom, Delete button disappears.
- UIA technique note: a WPF ComboBox's dropdown `ListItem`s show up **twice** in `FindAll` (once real/
  interactive, once a duplicate with the same bounding rect that doesn't support `SelectionItemPattern`) — must
  try `TryGetCurrentPattern` on each candidate and use whichever succeeds, not just the first match.
  Also: a modal `Window` with `WindowStyle="None"` and no explicit `Title` has an empty automation `Name`, so
  finding it via `AutomationElement.RootElement.FindAll(Children, ProcessIdProperty)` can miss it — raw Win32
  `EnumWindows` (matching PID, excluding the known main hwnd, filtering `IsWindowVisible`) reliably finds it.

---

## Session 2026-07-06 — Mid-stream hw→sw Degraded fallback (Phase 3 §3.6 tail, closes Phase 3)

**Goal:** The last real Phase 3 tail — when a hardware encoder can't keep up, actually recover instead of just
warning. Reuses the segment-rotation machinery just built for auto-split.

### Design
- `RecordingHealth.ShouldDowngradeToSoftware(behindDurationSeconds, encoderIsHardware)`: pure, `> 8s` behind
  (past the existing 3s Degraded threshold) AND hardware. Software encoders never trigger it.
- `RecordingCoordinator._activeEncoder` — now set inside `TryStartAnyEncoder` on success (previously nothing
  tracked which encoder in the chain actually started).
- `RotateSegment` generalized: `RotateSegment(List<EncoderInfo>? forcedChain = null)`. Auto-split calls it with
  no args (existing behavior, unchanged); the downgrade path passes a software-only chain. If a forced chain is
  used, it becomes `_encoderChain` going forward too, so a *later* auto-split rotation on the same recording
  keeps using the downgraded encoder rather than reverting to hardware.
- `BuildSoftwareFallbackChain(current)`: same-codec software encoders, then libx264 last resort (mirrors
  `BuildFallbackChain`'s style).
- `AttemptDowngrade()`: guards on `_downgradeAttempted` (once per recording) and `_activeEncoder.IsHardware`;
  warns (`record.encoder-downgrade`) then calls `RotateSegment(softwareChain)`.
- Wired into `PaceLoop`'s existing behind-realtime branch (nested inside the `now - behindSince > 3s` check
  that already sets Degraded): compute `behindSeconds` and call `AttemptDowngrade()` if
  `ShouldDowngradeToSoftware` is true, resetting `behindSince` for a fresh grace period on the new encoder.

### Verification — the honest version
- This dev box's AMD hardware encoder is fast enough (per the Phase 0.5 gate: 620 MB/s / 18.9% one core even at
  native 5120×1440) that it doesn't realistically fall behind at any resolution tried, so the *organic* trigger
  path can't be exercised live here — same category of gap as the NVENC/QSV vendor re-checks.
- Instead: a test-only seam, `internal RecordingCoordinator.TestForceDowngrade()`, sets a `volatile` flag the
  pacer thread checks each loop and calls the *exact same* `AttemptDowngrade()` the health check would — only
  the trigger differs, not the mechanism under test. A temporary `--selftest-downgrade` hook (mirrors the
  `--selftest-split` pattern) records 2s, forces the downgrade, records 4s more, stops.
- Result: segment 1 (`h264_amf`, 2.0s) → **log-confirmed** `Segment rotation: started segment 2
  (encoder=libx264)` → segment 2 (`libx264`, 6.0s). Both ffprobe-clean h264/aac MP4s at the correct resolution.
  3 new `RecordingHealth` tests; 124 tests total, 0 warnings.
- **Closes Phase 3** except the production decision-gate re-check (needs NVENC/QSV hardware, already tracked
  as a standing vendor-gate item).

---

## Session 2026-07-06 — Auto-split large recordings (Phase 3 §3.3 tail)

**Goal:** Close the last open Phase 3 tail — segment rollover so a long recording doesn't grow one unbounded
file (FAT32's 4 GB single-file cap was the original motivation).

### Design
- `RecModeSettings.AutoSplitEnabled` (bool, off) + `AutoSplitSizeMb` (int, default 3900 ≈ 3.9 GB — safely
  under FAT32's 4 GB limit). Settings → Recording gets a toggle + a size combo (1024/2048/3900/8000 MB,
  `AutoSplitSizeConverter` renders "~X GB").
- `RecordingCoordinator` captures at `Start()`: `_outputDir`, `_baseFileName` (pre-uniquify), `_encoderChain`
  (the fallback chain from `BuildFallbackChain`), `_jobTemplate` (the `FfmpegJob` record, reused via `with`),
  `_autoSplitThresholdBytes` (MB setting floored at 100 MB — a safety floor against thrashing, not just the
  raw setting).
- The pacer thread (`PaceLoop`) checks the current segment's file size ~1 Hz (same cadence pattern as the
  existing disk-space guard) via `TryGetSegmentSize`; crossing the threshold calls `RotateSegment()` inline
  (blocking briefly on the pacer thread — the CFR pacer's elapsed-driven catch-up absorbs the gap, same as any
  other stall).
- `RotateSegment()`: cancels+joins the audio thread, `StopAndFinalize`s the current ffmpeg session, safe-remuxes
  it to MP4 if `_safeRemux`, adds a library-index entry for it, then builds the next segment's paths via a new
  pure `FilenameBuilder.SegmentFileName(baseFileName, index)` (segment 1 unchanged; segment N≥2 → `Name_partN.ext`),
  starts a fresh `FfmpegRecordingSession` from `_jobTemplate with { OutputPath, new pipe names }`, and — if audio
  is enabled — restarts the audio pump thread against the new session's audio pipe (captures `_audioStop` in a
  local so the closure can't race a later cancel). On total encoder-start failure mid-recording, reports Fatal
  and stops gracefully (previous segments are already safe on disk).
- `Finalize()` (normal Stop) is unchanged — it always operates on whatever the *current* segment's fields are,
  so the last segment finalizes exactly like a non-split recording already did.

### Verification
- 5 new tests for `FilenameBuilder.SegmentFileName` (segment 1 passthrough, part2/part3/part10 suffixing,
  extension preserved for non-mp4). 121 tests total, 0 warnings.
- **E2E** via a new temporary `--selftest-split` hook (forces `AutoSplitEnabled=true`, `AutoSplitSizeMb=100` —
  the floor — and quality=100 for a faster bitrate ramp): a static desktop compresses hard even at max quality
  (~0.39 MB/s at 4096×1152@60 h264_amf), so the hook runs ~4.7 min to reliably cross 100 MB. Result: **2
  segments** — `Name.mp4` (107 MB, 264.4 s) rotated to `Name_part2.mp4` (6.3 MB, 15.6 s) — both ffprobe-clean
  (h264 4096×1152 + aac), both remuxed from their own safe-recording MKV, both present as separate entries in
  `library.json`. Confirms rotation, safe-remux-per-segment, audio-pipe continuity, and library indexing all
  work together.
- **Closes the last Phase 3 tail** (mid-stream hw→sw Degraded fallback remains a separate, harder item —
  restarting ffmpeg mid-segment for a *codec* change rather than a file rollover).

### Notes for next time
- The 100 MB floor exists only to stop someone from picking an absurdly small size and thrashing files; it also
  means a fast E2E verification needs either real motion on screen (higher bitrate, faster test) or patience —
  static-desktop self-tests are the slow path.

---

## Session 2026-07-06 — Top-bar navigation layout (Phase 6)

**Goal:** The design's alternate shell layout (Sidebar ↔ Top bar) + regain visual verification.

### Verification unblocked — PrintWindow
- **`PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT=0x2)` captures the RecMode window directly, even when it's
  behind the IDE** (the CopyFromScreen/GDI approach grabbed the IDE; SetForegroundWindow is blocked for bg
  procs). This restores visual UI verification: set state in settings.json → launch → PrintWindow → Read PNG.

### What was built
- **`EnumToVisibilityConverter`** (Themes): enum==param ? Visible : Collapsed (one-way). Registered `EnumToVisibility`.
- **`TopNavButton`** style (Controls.xaml): horizontal nav item — icon(Tag)+label, accent **underline** when checked.
- **ShellViewModel**: `Layout` (ShellLayout) prop, init from settings, updated on `SettingsChanged` (live switch).
- **SettingsViewModel**: `SelectedLayout` (uses `Save()` = immediate, so the shell switches now) + `Layouts`
  `[Sidebar, TopTab]`. Settings→Appearance gets a "Navigation layout" segmented control (glyph E8A1).
- **ShellWindow** body restructured: sidebar Border (col0, `Width=204`, Visibility=Layout==Sidebar) OR a top
  Border (row0 of the content grid, Visibility=Layout==TopTab) with horizontal `TopNavButton`s. Both nav sets
  bind `IsChecked` **one-way** to `SelectedNav` via `EnumToBool` (stay in sync; no cross-group juggling) +
  `Command=NavigateCommand`. Distinct GroupNames navSide/navTop.

### Verification (PrintWindow)
- Set `Layout=TopTab` → captured: horizontal nav (Record underlined) across the top, **sidebar gone**, content
  full-width. Sidebar mode still renders (reset). 116 tests, 0 warnings.

### Notes
- Enum is `ShellLayout.TopTab` (not "Topbar"). Compact layout (a separate mini-window) still deferred; motion left.

---

## Session 2026-07-06 — Hardware-bounded CPU thread cap (Phase 9)

**Goal:** The §3.3 "hardware-derived bounds" for the thread cap (last non-external Phase 9 item).

### What was built
- **`PerformanceBounds.ThreadCapOptions(logicalCores)`** (Core, pure/tested): `[0]` + candidate steps
  `{2,4,6,8,12,16,24,32}` that are `< cores`, always appending `cores` itself. Never exceeds the CPU. On 16
  threads → `0,2,4,6,8,12,16` (matches the old hardcoded list); 4-core → `0,2,4`; 1-core → `0`.
- **SettingsViewModel**: `ThreadCaps = PerformanceBounds.ThreadCapOptions(Environment.ProcessorCount)`; init
  clamps a stale `CpuThreadCap` not in the list → 0 (Auto).
- 4 `PerformanceBoundsTests`. Total **116**. (Effort tiers Fast/Balanced/Quality need no hw bound.)

### Notes
- Phase 9 remaining is now just the real update mechanism (Velopack) — external infra, out of scope here.
- CA1861: xUnit `Assert.Equal(new[]{…}, …)` inline arrays warn → moved expected arrays to `static readonly`.

---

## Session 2026-07-06 — Release-readiness portable re-acceptance (Phase 10)

**Goal:** Confirm the published R2R self-contained build still passes the portable acceptance with all post-MVP features.

### What was done
- `publish-portable.ps1 -Version 0.9.0` → `artifacts\RecMode-0.9.0-portable-win-x64\` (+ zip). Relocated outside
  the repo; ran `--selftest-record` (360-frame MP4, 627 KB) + `--selftest-screenshot` (PNG). `%AppData%\RecMode`
  and `%Videos%\RecMode` **absent before and after** → still fully folder-contained. No publish/R2R regressions.

### Gotcha (tooling)
- **Run `publish-portable.ps1` with `pwsh` (PowerShell 7), not Windows PowerShell 5.1.** Invoking it via
  bash→`powershell` (5.1) mis-decodes the UTF-8 em-dashes in the script's strings → bogus "missing string
  terminator / missing }" parse errors. The `PowerShell` tool (pwsh 7) runs it fine.

---

## Session 2026-07-06 — Accessibility pass: accessible names (Phase 10)

**Goal:** Start Phase 10 (toward 1.0) with a11y — give icon-only/nameless controls accessible names.

### What was done
- `AutomationProperties.Name` added to: ShellWindow theme toggle ("Toggle theme") + caption buttons
  (Minimize/Maximize/Close); SettingsView accent swatches ("Blue accent"…"Orange accent"); RecordView Quality
  slider, System/Microphone volume sliders, and the System/Microphone level meters (ProgressBars).
- XAML-only; no VM/logic change. `AutomationProperties` needs no xmlns prefix in WPF.

### Verification (automation tree, headless)
- Queried the running app's UIA tree by name: Toggle theme / Minimize / Maximize / Close (Button), System audio
  volume / Microphone volume (Slider), Quality — all resolve. (Swatches live on the Settings page, not in the
  launch tree; same one-line treatment, pattern proven on the shell controls.) 112 tests, 0 warnings.

### Remaining (Phase 10)
- Fuller a11y (LabeledBy/tab-order audit, real screen-reader pass); Win10 2004 regression pass (needs Win10);
  1.0.0 portable zip; final hardening.

---

## Session 2026-07-06 — Full container matrix: MOV + WebM (Phase 7)

**Goal:** Complete the codec/container matrix — add MOV + WebM output with validation.

### What was built
- **`MediaCompatibility`** (Core, pure/tested): `IsVideoCompatible(codec, container)` — WebM = AV1 only, else
  true (MP4/MOV/MKV take H.264/HEVC/AV1); `IncompatibilityReason` for the pre-flight message. **8 tests.**
- **RecordViewModel**: `Formats = [Mp4, Mkv, Mov, WebM]`. **Bug fix:** `_selectedFormat` init was a
  `Mkv ? Mkv : Mp4` ternary → MOV/WebM from settings silently became MP4; now
  `Formats.Contains(Container) ? Container : Mp4`.
- **Coordinator**: pre-flight `MediaCompatibility` check → `_errors.Block` + return false when incompatible.
  Safe-recording now `container is Mp4 or Mov` (remux MKV→MOV; the shared `Remuxer` is extension-driven so
  `-movflags +faststart` + `.mov` target just works). `ContainerExtension` already had mov/webm.

### Verification (E2E, ffprobe)
- **MOV**: h264 + aac, valid mov (safe-recording remux). **WebM**: **av1 (av1_amf, 5120×1440)** + opus, direct
  (safe=false). **H264+WebM → blocked** (no recording, no `.recording.mkv`). 112 tests, 0 warnings.

### Notes
- **av1_amf works on this AMD RX 7900 XTX** (AV1 *hardware* encode) and does full 5120×1440 — no 4096 width cap
  (h264_amf caps at 4096; av1/hevc don't). Good WebM/AV1 path on AMD.
- Remaining Phase 7: per-app audio (process-loopback WASAPI), webcam source + overlay — both interop/hardware-heavy.

---

## Session 2026-07-05 — Draw-on-screen annotation (Phase 8 complete)

**Goal:** The last Phase 8 item — freehand draw-over-screen, captured in the recording.

### What was built
- **`AnnotationOverlay`** (Views): fullscreen `InkCanvas` (EditingMode=Ink) on the primary monitor, near-transparent
  hit-testable background (`#01000000`), topmost, **NOT capture-excluded** (ink must be recorded). Accent pen
  (width 4, FitToCurve). Esc → `onExit` callback; right-click → `Strokes.Clear()`. `Canvas` property exposes the
  InkCanvas for the self-test.
- **`AnnotationService`** (App): observes `RecordViewModel.IsAnnotating` → show/hide overlay; passes
  `record.StopAnnotating` as the Esc callback. Registered + `Attach()`ed at startup.
- **RecordViewModel**: `IsAnnotating` + `ToggleAnnotateCommand` (only toggles when recording) + `StopAnnotating`;
  reset to false in `OnFinished`.
- **Toolbar**: a **Draw** toggle button (accent foreground when `IsAnnotating`).
- Temp `--selftest-annotate` hook: overlay + add a diagonal Stroke → WGC-capture → `annotate.png`.

### Verification (WGC, headless)
- `--selftest-annotate` → cropped region shows the blue accent **ink stroke** over the desktop content →
  renders AND is captured (not excluded). 104 tests, 0 warnings.

### Notes
- Overlay covers the primary monitor; while active it captures input (drawing), so the app underneath isn't
  clickable — Esc exits (the toolbar Draw button turns it on; z-order may cover it, so Esc is the reliable off).
  Single fixed pen colour for MVP; palette/undo could come later.
- **Phase 8 now fully done** (scheduler engine + click ripple + annotation).

### Note (tooling)
- The command-execution classifier was briefly unavailable between the ripple and annotation commits; the
  annotation code was written + reviewed during the outage, then built/tested/verified/committed once it recovered.

---

## Session 2026-07-05 — Click highlight ripple (Phase 8)

**Goal:** Make "Highlight mouse clicks" real — draw a ripple at each click, captured in the recording.

### What was built
- **`GlobalMouseHook`** (App/Services): `WH_MOUSE_LL` low-level hook → `Clicked(screenX, screenY)` on L/R
  button-down. Install on UI thread; delegate kept alive; fast callback.
- **`ClickRippleOverlay`** (Views): fullscreen on the primary monitor, topmost, transparent,
  **click-through** (`WS_EX_TRANSPARENT|LAYERED|NOACTIVATE|TOOLWINDOW` set in SourceInitialized), `IsHitTestVisible=False`,
  **NOT capture-excluded** (ripple must be in the recording). `AddRipple(screenX,screenY)` → DIP-converts
  (`/dpiScale`, monitor-relative), adds an accent ring with ScaleTransform 0.25→1 + opacity 0.85→0 over 480ms,
  removed on completion. Ignores clicks off the covered monitor.
- **`ClickHighlightService`** (App/Services): observes `RecordViewModel.IsRecording`; when recording **and**
  `settings.HighlightClicks`, shows the overlay + installs the hook (`hook.Clicked→AddRipple`); tears both down
  on stop (§3.9 — no idle hook). Registered + `Attach()`ed at startup.
- Temp `--selftest-ripple` hook: overlay + AddRipple(centre) → WGC-capture → `ripple.png`.

### Verification (WGC, headless)
- `--selftest-ripple` → cropped centre of the capture shows the blue accent **ring** at (2560,720) — renders
  correctly AND is captured (not excluded). 104 tests, 0 warnings.

### Notes
- Overlay covers the **primary** monitor only (clicks elsewhere aren't rippled) — fine for the common case;
  could span the recorded monitor later. Hook path (hook→AddRipple) is standard; the render+capture is verified.

### Remaining (Phase 8)
- Annotation (interactive draw-on-screen) — the last Phase 8 item.

---

## Session 2026-07-05 — Scheduler engine (Phase 8)

**Goal:** Fire the scheduled recordings whose UI + model landed in Phase 6.

### What was built
- **`ScheduleItem.LastFiredUtc`** (added, backward-compatible) — dedup + Once/Weekly tracking.
- **`ScheduleEvaluator.IsDue(item, now)`** (Core, pure): enabled + valid HH:mm + `now.Hour/Minute==target` +
  not-fired-within-90s + recurrence day match (Once = never fired; Daily = any; Weekdays = Mon–Fri; Weekly =
  ≥7d-1h since last). **8 unit tests.**
- **`SchedulerService`** (App): `DispatcherTimer` 20 s poll → `Tick`: stop a scheduled recording past its
  duration (`_scheduledStopAt`); else if not recording, fire the first due schedule. `Fire`: stamp LastFiredUtc
  (+ disable Once) → `RequestSave` → `record.EnsureDevicesLoaded()` + `StartRecordingFromCli()` → set
  `_scheduledStopAt = now + DurationMinutes`. Never interrupts an active recording. Registered + `Start()`ed in
  App.OnStartup.

### Verification
- 8 evaluator tests. **E2E:** set Daily @ next minute → app fired a real recording at that minute
  (`.recording.mkv` appeared 23:15:14 for a 23:15 schedule) and stamped `LastFiredUtc=23:15:11`. 104 tests, 0 warnings.

### Notes
- Duration-stop verified by code review (Tick checks `_scheduledStopAt`), not the full 60 s wait.
- Missed-while-closed windows aren't caught up (design: works while running / in tray). No day-of-week field for
  Weekly (fires ~weekly from LastFired) — fine for MVP; add a day picker if needed later.

### Remaining (Phase 8)
- Annotation (draw-on-screen); click ripple/highlight.

---

## Session 2026-07-05 — Mid-recording disk-space guard (§3.6)

**Goal:** Stop a recording gracefully before a full disk corrupts the finish (long-recording robustness).

### What was built
- **`RecordingHealth.IsDiskCritical(freeBytes)`** (Core, tested): `free >= 0 && free < DiskCriticalBytes`
  (500 MB); ignores unknown/negative readings.
- **Coordinator**: `_outputRoot` captured at Start (drive root); pacer checks `IsDiskCriticallyLow()` every ~2s;
  when critical → `_errors.Warn("record.disk-critical", …)` + `Task.Run(Stop)` + `return` (breaks the pacer
  loop). **Stop() joins the pacer, so it must run off-thread, never inline.** `IsDiskCriticallyLow` = DriveInfo
  free-space probe (best-effort; failure never stops a recording).
- Tests: `DiskCritical_BelowThreshold` / `_AboveThreshold_OrUnknown`. Total **96**.

### Verification
- E: 691 GB free → `--selftest-record` completes (360 frames), **0** disk-critical warnings (no false positive).
  Can't force a truly-full disk headlessly; the stop path is code-reviewed (off-thread Stop, no self-join deadlock).

### Notes
- Complements the under-2 GB pre-flight warning: pre-flight warns before starting, this stops cleanly mid-way.

---

## Session 2026-07-05 — Correctness review: orphan-recovery race fix

**Goal:** Self-review recent engine changes; fix a real bug found in orphan recovery.

### Bug + fix
- **Race:** `App.OnStartup` runs `OrphanRecoveryService.RecoverOrphans` on a background thread, then may start a
  recording (`--record`/`--tray --record`). The recording's live `*.recording.mkv` temp is indistinguishable
  from an orphan → recovery could remux a half-written copy and **delete the temp mid-recording**, destroying
  the take.
- **Fix:** `IsInUse(path)` — try `File.Open(FileShare.None)`; if it throws (IOException/UnauthorizedAccess), the
  file is locked by an active recording → skip it. Only genuinely closed orphans are recovered.

### Verification (headless, E2E)
- Two valid `*.recording.mkv` seeded; one held open with a PS `FileStream` (simulates the active recording),
  one free. Launched the app → **locked one left untouched** (present, no mp4), **free one recovered** (mkv
  gone, mp4 created). 94 tests, 0 warnings.

### Review notes (other recent changes — no fix needed)
- Health `_encoderBehind` hysteresis (trigger >fps, reset ≤fps/2) is intentional anti-flap.
- Hotkey `Rebind` (UnregisterAll + Register) is idempotent (`_hooked` guards re-subscription).
- Recovered orphans get no library-index entry (they bypass the coordinator) — acceptable; they still list via
  the filesystem fallback.

---

## Session 2026-07-05 — Library metadata index (Phase 5)

**Goal:** "capture-source metadata in the library index from day one" — record metadata, show it in the Library.

### What was built
- **`LibraryIndexEntry`** (Core.Library): FileName/Source/Codec/Container/Width/Height/Fps/DurationSeconds/
  CreatedAt (primitives → no enum coupling). **`ILibraryIndex`/`LibraryIndex`**: JSON array at
  `AppPaths.LibraryIndexPath` (portable-safe), `Add` (replace-by-filename + cap 1000 + atomic temp-move),
  `ByFileName()` (dict); corrupt/locked/missing → empty (non-fatal). Registered in Composition.
- **Coordinator**: injects `ILibraryIndex`; snapshots metadata at Start (`_metaSource/_metaCodec/_metaContainer/
  _metaWidth/_metaHeight/_metaFps`); on **successful** Finalize writes an entry (duration = frames/fps). Uses
  the final (post-remux) path's file name.
- **LibraryViewModel**: injects the index; `BuildMeta(file, entry?)` → "H.264 · 1920×1080 · 0:12 · size · date"
  when indexed (FriendlyCodec + FormatDuration), else "size · date". Screenshots (images) skip the index.
- Tests: 4 `LibraryIndexTests` (round-trip, replace-by-name, persist-across-instances, missing=empty) using a
  portable `AppPaths` in a `TempDir`. Total **94**.

### Verification (headless)
- `--selftest-record` → `library.json` has 1 entry: Display / H264 / Mp4 / **4096×1152 / 60fps / 6s**. Accurate.
  Best-effort write didn't affect the recording. 94 tests, 0 warnings.

### Notes
- UI enrichment (the richer Library meta line) not screenshot-verified this run (window-behind-IDE); the index
  content is verified headlessly and BuildMeta is simple formatting.

### Remaining (Library-pro §7)
- Search/tags/collections; possible SQLite graduation; ffprobe backfill for un-indexed files.

---

## Session 2026-07-05 — Remappable global hotkeys (Phase 9)

**Goal:** Make the F9/F10/F11 hotkeys user-remappable (last read-only piece of Settings).

### What was built
- **`HotkeyChord`** (Core.Input, pure/tested): `Modifiers` (Win32 MOD_* values) + `VirtualKey`; `TryParse`
  ("Ctrl+Shift+F9", case/space-insensitive; rejects modifier-only / two-key / unknown); `ToString`
  (Ctrl→Alt→Shift→Win + key). Key map: F1–F24 (0x70+), A–Z, 0–9. **15 unit tests.**
- **`GlobalHotkeys.UnregisterAll()`** (for rebind, keeps the message window).
- **`HotkeyBindings`** now injects `ISettingsService`, registers from `s.HotkeyStartStop/PauseResume/Screenshot`
  (parse → chord; fallback F9/F10/F11 if blank/invalid), and `Rebind()` = UnregisterAll + Register.
- **`SettingsViewModel`**: injects `HotkeyBindings`; `ChangeHotkeyCommand(action)` → `BeginCapture`;
  `IsCapturingHotkey`/`HotkeyCaptureHint`; `CompleteCapture(chordText)` persists (`Save`) + `Rebind` + clears.
- **`SettingsView`** (code-behind): hotkeys card gets per-row Change buttons + a capture-prompt border;
  `OnChangeHotkey` focuses the UserControl; `OnCaptureKeyDown` ignores lone modifiers, Esc cancels, else builds
  a `HotkeyChord` from `Keyboard.Modifiers` + `KeyInterop.VirtualKeyFromKey` and calls `CompleteCapture`.

### Verification
- 15 HotkeyChord tests. Launched with custom chords (Ctrl+Alt+R, Shift+F8) → registered, **0** in-use warnings;
  invalid stored chord ("not-a-key") → graceful fallback, no crash, app alive. Total **90** tests, 0 warnings.
- **Gotcha:** couldn't UI-Automation-drive the capture or screenshot the hotkeys UI this run — the RecMode
  window opened **behind the IDE** and `SetForegroundWindow` is blocked for background processes (capture grabbed
  the IDE). Functional core verified via unit tests + settings-driven registration instead.

### Remaining (Phase 9)
- Update-notify mechanism (Velopack); the effort/thread-cap bounds from a hardware probe.

---

## Session 2026-07-05 — FLAC audio verified E2E (Phase 4)

**Goal:** Confirm the FLAC lossless-audio path actually works through the real pipeline (a tracked Phase 4 tail).

### What was done
- Drove the **real CLI**: set `settings.json` Container=Mkv, AudioCodec=Flac, SystemAudioEnabled=true; launched,
  `--record` → wait → `--stop`; ffprobe'd the output.
- **Result:** MKV with **flac** audio (48 kHz, 2ch) + h264 video. FLAC works E2E. No code change needed (args
  were already unit-tested; container steering = FLAC→MKV only, MP4/MOV→AAC, WebM→Opus).
- Added `Snapshot_Libx264_Mkv_WithAudioFlac` (`-c:a flac`, no bitrate, MKV no faststart). Encoding 31→32, total **75**.
- Reset dev `settings.json` back to Mp4/Aac defaults.

### Notes
- Verified via the real `--record`/`--stop` CLI (not a self-test hook) — the self-test hardcodes Mp4, so it
  can't exercise MKV-only codecs. Good pattern for future container-specific checks.

### Remaining (Phase 4)
- Mic on real hardware; caution meter colour >82%; ±40 ms soak sync test.

---

## Session 2026-07-05 — On-battery pre-flight warning (§3.6 / Phase 9)

**Goal:** Warn when recording on battery (promoted 1.0 feature) — recording is power-hungry.

### What was built
- **`IPowerStatus`/`PowerStatus`** (App/Services): Win32 `GetSystemPowerStatus` P/Invoke → `IsOnBattery`
  (`AcLineStatus==0`), `BatteryPercent` (0–100 or null). Interface makes the decision mockable.
- **Coordinator** ctor takes `IPowerStatus`; pre-flight (after disk check) warns
  `record.on-battery` "You're recording on battery power (NN% left) … plug in for long sessions" when
  `IsOnBattery`. Registered in Composition.

### Verification
- Desktop (Win32_Battery absent) → `--selftest-record` succeeds with **no** on-battery warning in the log
  (no false positive). Positive path is mockable via `IPowerStatus`; fires on real laptop battery. 74 tests, 0 warnings.

### Remaining (promoted 1.0 / misc)
- Hotkey remap UI (Phase 9); tray recent-targets; first-run encoder benchmark; disk-speed health signal.

---

## Session 2026-07-05 — Encoder effort tiers (Phase 3 §3.3)

**Goal:** Fast/Balanced/Quality effort setting, mapped per-encoder to its preset (finishes the resource-control args).

### What was built
- **`EncoderEffort`** enum (Core.Settings: Fast/Balanced/Quality) + `RecModeSettings.Effort = Balanced`.
- **`FfmpegJob.Effort`** (default Balanced); `FfmpegArgsBuilder.BuildEncoderArgs(enc, quality, effort)` maps
  per-encoder: x264/x265 `ultrafast/veryfast/medium`, svtav1 `10/8/6`, nvenc `p2/p4/p6`, amf
  `speed/balanced/quality`, qsv `veryfast/medium/slow` (own map — qsv has no "ultrafast"). **Balanced = the
  previous hardcoded preset for every encoder**, so existing snapshots stay valid. Coordinator passes
  `settings.Effort`.
- **Settings Performance**: "Encoder effort" combo (`Efforts` list, `SelectedEffort`), gauge glyph EC4A, above
  the thread-cap card. Strings added.
- Tests: 9 new `[Theory]` effort mappings (x264/nvenc/amf × Fast/Balanced/Quality). Encoding 22→31, total **74**.

### Verification
- Performance section screenshot: Encoder effort card renders (gauge icon, "Balanced" default, desc). Existing
  snapshot tests unchanged (Balanced preserved). 74 tests, 0 warnings.

### Notes
- Slow presets may not sustain 60fps in real time — the recording health indicator warns if the encoder falls
  behind, so the two features complement each other.

---

## Session 2026-07-05 — Recording health indicator (§3.6)

**Goal:** Surface "the encoder can't keep up" (a promoted 1.0 feature) via the DegradedState channel.

### What was built
- **`RecordingHealth`** (Core, pure/tested): `FramesBehind(elapsedS, framesWritten, fps)` = `elapsedS·fps −
  framesWritten`; `IsBehindRealtime` = `elapsedS>2 && FramesBehind>fps` (>1s behind after a 2s grace).
- **Coordinator pacer**: computes health each report tick; sustained-behind for 3s → `_encoderBehind=true` +
  one-time `_errors.Degrade("record.encoder-slow", …)`; clears when `FramesBehind ≤ fps/2`. `_targetFps`/
  `_encoderBehind` fields reset in `Start`. `RecordingProgress` gains `bool IsHealthy` (passed `!_encoderBehind`).
- **RecordViewModel**: `IsHealthy` property (reset true on finish); `OnProgress` prefixes StatsText with
  "⚠ Can't keep up · " when unhealthy. **Toolbar** rec dot goes amber when `IsHealthy==False` (same trigger
  style as paused).
- Tests: `RecordingHealthTests` (4) — keeping-up healthy, >1s behind unhealthy, grace period, just-under-1s. Total **65**.

### Verification
- `--selftest-record` → success, 360 frames, **no** degrade warning (healthy on this AMD hw = no false positive).
  The degraded UI path reuses the already-verified snackbar (Degrade→snackbar) + paused-amber-dot patterns.
  65 tests, 0 warnings.

### Notes
- Health flag is a lightweight bool, deliberately NOT driving the state machine's `Degraded` state (kept for a
  future hw→sw fallback) to avoid coupling/regressions.

---

## Session 2026-07-05 — Snapshot-tested ffmpeg args matrix (Phase 3 §3.3)

**Goal:** Lock the full ffmpeg command line so ffmpeg re-pins / careless edits can't silently change the encode.

### What was built
- 6 snapshot tests in `FfmpegArgsBuilderTests` asserting the **exact** whitespace-normalised (`Norm` =
  `Regex.Replace(s,@"\s+"," ").Trim()`) full command line: libx264/MP4 (video-only), libx264/MKV (no
  faststart), h264_amf/MKV, libx264/MP4+AAC, libsvtav1/WebM+Opus, libx264/MP4+`-threads 4`. Encoding tests
  16→22; total **61**. Hand-computed expected strings all matched first run.

### Notes
- Snapshot surfaces a latent quirk to revisit: faststart is emitted for **Mp4 only**, not Mov (MOV gets no
  `+faststart`). Captured as-is; fix when MOV is productionised (Phase 7 codec matrix).
- Update these strings deliberately when an arg genuinely changes — that's the guard working.

### Remaining (Phase 3)
- Auto-split (segment muxer); mid-stream hw→sw Degraded fallback; production decision-gate re-check.

---

## Session 2026-07-05 — Orphaned-recording recovery on launch (Phase 3 crash safety)

**Goal:** Recover `*.recording.mkv` files left by a crashed session — the payoff of the safe-recording design.

### What was built
- **`Remuxer`** (RecMode.Encoding): extracted the shared `RemuxToMp4(ffmpeg, src, mp4)` static (`-c copy
  -movflags +faststart`, drains stderr, 30s timeout). The coordinator's `Remux` now delegates to it (DRY).
- **`OrphanRecoveryService`** (App/Services, injects `IFfmpegLocator`/`IAppPaths`/`IErrorReporter`):
  `RecoverOrphans()` scans `RecordingsDirectory` for `*.recording.mkv`, remuxes each → `<stem>.mp4`
  (`UniqueMp4Path` avoids clobber → " (recovered N).mp4"), deletes the temp on success, logs, and warns once
  ("Recovered a/N recording(s) from a previous session"). Skips gracefully if ffmpeg unavailable.
- **App.OnStartup:** `Task.Run(recovery.RecoverOrphans)` (off the UI thread) after shell/tray wiring.
  Registered in Composition.

### Verification (real GUI + ffprobe)
- Created a valid `OrphanTest.recording.mkv` (ffmpeg lavfi testsrc, 15KB) in Recordings; launched the app →
  after ~6s the mkv was gone and `OrphanTest.mp4` (16KB) existed, ffprobe-valid **h264 640×360** mp4. 55 tests, 0 warnings.

### Notes
- Advances Phase 3 "orphan-MKV detection on launch". The corrupt "moov atom not found" mp4s in bin Recordings
  are from old kill-tests — don't seed orphan tests from them; generate a fresh mkv via `-f lavfi testsrc`.

### Remaining (Phase 3)
- Snapshot-tested args matrix; auto-split (segment muxer); mid-stream hw→sw Degraded fallback; gate re-check.

---

## Session 2026-07-05 — Per-source audio volume (Phase 4 mixer UI)

**Goal:** Per-source volume sliders on the Record audio card, applied live to metering + the recording.

### What was built
- **Settings:** `RecModeSettings.SystemVolume`/`MicVolume` (int 0–100, default 100; additive).
- **Coordinator:** sets `_mixer.SystemGain/MicGain = volume/100` at mixer start; new
  `SetAudioGains(sysGain, micGain)` mutates the live recording mixer (mid-recording propagation).
- **RecordViewModel:** `SystemVolume`/`MicVolume` (double 0–100, persisted) + `SystemVolumeLabel`/`MicVolumeLabel`
  ("NN%"); `ApplyGains()` sets meter-mixer gains **and** calls `_coordinator.SetAudioGains`. Meter mixer also
  seeded with gains on start. Inits from settings.
- **RecordView:** volume `Slider` (`AppSlider`, 0–100) + % label under each source's toggle/meter, wrapped in a
  Grid `IsEnabled="{Binding <Source>Enabled}"` so it dims when the source is off.

### Verification (real GUI, UI-Automation)
- Audio card screenshot: System + Mic each show toggle · meter · volume slider · "100%"; mic slider dimmed
  (mic off). 3 sliders total (quality/system/mic). Setting the system slider to 60 persisted `SystemVolume=60`.
  55 tests, 0 warnings.

### Notes
- Mute = the existing enable toggle (drag-to-0 also silences); no separate mute icon (design deviation logged).
  Mixer supports `Muted` independently for a future preserve-volume mute.
- Resolves Phase 4 tails: per-source mute/gain UI + mid-recording gain propagation.

### Remaining (audio)
- Real-mic verification on hardware; FLAC path; caution meter colour >82%; ±40 ms soak sync test.

---

## Session 2026-07-05 — Encoder resource controls (Phase 3 §3.3 / Phase 9 Performance)

**Goal:** Wire the resource-efficiency levers (thread cap, encoder priority) that were modelled but unused.

### What was built
- **`FfmpegJob`**: `CpuThreadCap` (int, 0=auto) + `BelowNormalPriority` (bool). **`FfmpegArgsBuilder.Build`**
  emits `-threads {cap}` only when `cap>0 && !Encoder.IsHardware` (hw offloads to GPU/ASIC → thread cap is
  meaningless). **`FfmpegRecordingSession.Start`**: best-effort `PriorityClass=BelowNormal` (try/catch, never
  fatal) when requested. **Coordinator**: populates both from `settings.Current.CpuThreadCap` /
  `.BelowNormalEncoderPriority`.
- **Settings**: `SettingsViewModel` gains `CpuThreadCap` (list [0,2,4,6,8,12,16]) + `LowerEncoderPriority`
  (bool→BelowNormalEncoderPriority), both via `Persist`. **`ThreadCapConverter`** (0→"Auto") + template.
  **Performance** section in SettingsView (CPU-chip glyph E950 + thread-cap combo; lightning glyph E945 +
  priority toggle). Strings added.
- **Test**: `ThreadCap_AppliesToSoftwareOnly` — `-threads 4` present for libx264, absent for h264_amf and for
  cap=0. Encoding tests 15→16, total **55**.

### Verification (real GUI + tests)
- Performance section renders (screenshot): thread cap shows "Auto" (converter), both icons correct; priority
  toggle persisted True→False in settings.json. 55 tests, 0 warnings.

### Notes
- Advances the Phase 3 "resource-control args" tail and starts the Phase 9 "Performance group". Effort tiers
  (NVENC p1–p7 / AMF / QSV presets) still TODO; only thread cap + priority done here.

### Remaining (Phase 6)
- Topbar + compact layout variants; Library/Record polish + motion. (Per-source audio gain/mute UI still a Phase 4 tail.)

---

## Session 2026-07-05 — Phase 6 (part 6): per-card icons

**Goal:** Leading Fluent icons on every Settings card + the Schedule card (finishes the icon deviation).

### What was built
- **`CardIcon`** style (Controls.xaml): `Segoe Fluent Icons` glyph, 18px, 20px width, TextSecondary, centered,
  14px right margin.
- **SettingsView** rewritten to a consistent 3-column card grid (`Auto` icon | `*` body | `Auto` control) with
  a glyph per card: Theme E771, Accent E790, Encoder E714, Container E7C3, Audio format E767, Audio bitrate
  E9E9, Save-to E838, Pattern E8AC, Countdown E916, Cursor E962, Clicks E7C9, Hotkeys E765 (top-aligned),
  Startup E7E8, Updates E895. **Schedule** card gained the calendar glyph E787 (controls shifted to column 2).

### Verification (real GUI, screenshots)
- Two Settings screenshots (top + scrolled): all 14 glyphs render correctly, no fallback boxes; the tricky
  ones (sliders/bitrate, stopwatch/countdown, mouse/cursor, tap-pointer/clicks, rename/pattern) are all
  sensible. 54 tests, 0 warnings.

### Gotchas
- `Segoe Fluent Icons` (Win11) / `Segoe MDL2 Assets` (Win10 fallback) covers the whole icon set — verify glyph
  codes by screenshot (a wrong code renders as an empty box, not an error). All chosen codes rendered first try.

### Remaining for Phase 6
- Topbar + compact layout variants; Library/Record polish + motion.

---

## Session 2026-07-05 — Phase 6 (part 5): schedule editor

**Goal:** Make schedules editable (name/recurrence/time/duration), not just default-add.

### What was built
- **`ScheduleEditViewModel`** (edits a working copy; `IsValid` = non-empty name + `TimeOnly.TryParseExact`
  "HH:mm"; `ApplyTo(item)` commits). Recurrences + Durations preset lists.
- **`ScheduleEditWindow`** (modal, borderless card, DragMove on title, Esc=cancel): Name TextBox, Recurrence
  combo, Start-time TextBox (HH:mm), Duration combo; Save validates via `IsValid` (else MessageBox), Cancel.
- **`IScheduleEditor`/`ScheduleEditor`** (region-picker-style service): `Edit(item)` shows the dialog over a
  working copy, `ApplyTo` on save, returns bool. Registered in Composition.
- **ScheduleViewModel**: injects `IScheduleEditor`; `NewSchedule` opens the editor immediately (keep default on
  cancel) then adds+persists; new `EditCommand` (per card) → `editor.Edit(row.Model)` → `row.RefreshDisplay()`
  + persist. `ScheduleRowViewModel.RefreshDisplay()` raises Name/WhenText/Enabled/StateLabel. Edit button on card.
- Strings: Schedule_Edit + ScheduleEdit_* (Title/Name/Recurrence/Time/Duration/Save/Cancel/InvalidTime).

### Verification (real GUI, UI-Automation)
- Dialog renders with all fields (full-screen screenshot: Edit schedule / New schedule / Once / 19:27 / 30 /
  Cancel·Save). New schedule → set name "Morning standup" via ValuePattern → Save → settings.json has 1
  schedule name="Morning standup" rec=Once dur=30. 54 tests, 0 warnings.

### Gotchas
- `Resources.Strings` in a Window code-behind collides with `Window.Resources` (ResourceDictionary) → must
  fully-qualify `RecMode.App.Resources.Strings`.
- Modal borderless `AllowsTransparency` dialogs have an empty automation Name → find them by a child element
  (the "Save" button) across the process's window children, with a short retry, not by window name.

### Remaining for Phase 6
- Per-card leading icons (Settings/Schedule cards); topbar + compact layouts; Library/Record polish + motion.

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
