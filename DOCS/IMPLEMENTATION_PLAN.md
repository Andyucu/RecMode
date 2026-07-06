# RecMode — Implementation Plan (v2)

**App:** RecMode — a modern Windows 11 screen recorder (Bandicam-class)
**Stack:** .NET 10 · WPF · Windows 11 Fluent design · MVVM
**Design source of truth:** `DOCS/RecMode Screen Recording App/RecMode.dc.html` (+ `_ds/` design system)
**Status tracking:** `CLAUDE.md` (phase checklist) · `CHANGELOG.md`

> v2 changes (2026-07-03): added Phase 0.5 pipeline spike + throughput decision gate; slimmed Phase 1 so a real recording MVP lands before full design fidelity; explicit MVP 1.0-alpha definition; per-subsystem test strategy; error taxonomy in Core from day one; OS support matrix (Win11-first, Win10 2004+ supported); FFmpeg licensing recorded as a product decision. Same-day additions: **portable-first distribution** (§3.5 — self-contained folder, bundled ffmpeg, `.\Data` state) and **encoding resource controls** (§3.3 — CPU thread cap, encoder effort tiers, priority, within hardware-derived recommended bounds); **resource-efficiency principles + budgets** (§3.9); robustness/product-gap pass — HDR tone-mapping, CFR output policy, opt-in crash minidumps, auto-split, CLI automation, localization-ready strings, privacy stance, vendor-matrix testing risk, and an explicit post-1.0 backlog (§7).

---

## 1. Product scope (from requirements + design)

### Functional requirements
| Area | Requirement |
|---|---|
| Capture sources | Full screen (per display + *all displays*), single window, draggable/resizable region, webcam-as-source |
| Video codecs | AV1, HEVC/H.265, H.264 |
| Encoder backends | NVIDIA NVENC, AMD AMF, Intel QSV, software (x264 / x265 / SVT-AV1) — auto-detected, user-selectable |
| Containers | MP4, MKV, MOV, WebM |
| Frame rates | 30 / 60 / 120 fps |
| Quality | 0–100 slider mapped to CRF/CQ (`CRF = 51 − q·0.38`), live bitrate + MB/min estimate |
| Audio | Multiple simultaneous sources: system loopback, microphone(s), per-application audio; per-source volume slider, mute, live level meter; AAC 128/192/320 kbps or FLAC |
| Webcam | Record webcam as source, or overlay bubble on any recording; camera picker |
| Screenshots | Hotkey + button, PNG, flash effect, copy to clipboard, saved to library |
| Recording controls | Start/stop (F9), pause/resume (F10), screenshot during recording (F11), mic mute, on-screen annotation/draw, floating toolbar with live stats |
| Library | Videos + Screenshots tabs, thumbnail grid, codec/resolution/size/date metadata, play, share (Windows Share / copy link / email / cloud), open folder, delete to Recycle Bin, copy image |
| Schedule | Scheduled recordings (once/recurring, duration, source), armed/off toggle, runs from tray |
| Settings | Theme light/dark, 5 accent colors, encoder/container/audio defaults, output folder, filename pattern, 3-s countdown, cursor capture, click highlighting, remappable global hotkeys, start with Windows, update check |
| UX | 3 layouts: sidebar / top-tab / **compact launcher** (small always-on-top widget); resizable region & window; countdown overlay; toasts; tray icon; status pill in title bar |
| Encoding resources | User-tunable encoding budget **within recommended bounds derived from detected hardware**: CPU thread/core cap + speed↔quality preset for software encoders; GPU encoder effort tier (preset) for NVENC/AMF/QSV; optional below-normal encoder process priority (§3.3) |
| Distribution | **Portable-first**: one self-contained folder (app + ffmpeg + settings + logs), runs from anywhere incl. USB, no installer/registry required; installer + auto-update is a later add-on (§3.5) |
| Long recordings | Optional **auto-split** at a time or size boundary (FAT32-safe 4 GB default when recording to such a volume) via the ffmpeg segment muxer |
| Automation | **CLI arguments** (`--record`, `--screenshot`, `--stop`, source/duration selection) with single-instance argument forwarding (§3.5) |

### Non-functional
- **Resource efficiency is a core product value** (§3.9): the app must be near-silent when idle and lightweight while recording. Measured budgets, regression-checked in soak runs:

  | State | CPU | GPU | Memory |
  |---|---|---|---|
  | Idle in tray / minimized | < 0.5% (no timers, no capture) | 0 | < 180 MB private |
  | Record screen open (live preview) | < 3% | copy + present only | < 250 MB |
  | Recording 1440p60, hw encoder | app (excl. ffmpeg) < 15% of one core (§3.3 gate) | encode block + one copy chain | flat over 2 h (no growth) |

- 1440p60 with GPU encoder must not saturate one CPU core; throughput budget is a measured decision gate (§3.3).
- Crash-safe recordings (MKV remuxes fine after a crash; MP4 fragmented or finalized with `+faststart`).
- Light + dark theme, Mica window backdrop (Win11), Fluent motion; reduced-motion respected.
- **Portable by construction:** no state outside the app folder in portable mode; self-contained .NET publish (no runtime install needed); a later installer build reuses the same code with different path resolution.
- **Privacy:** no telemetry of any kind; nothing leaves the machine. Crash minidumps are **opt-in/configurable**, stored locally when enabled, and shared only by explicit user action. Stated plainly in About — for a screen recorder this is a product feature.
- **Localization-ready:** every UI string lives in resource files from the first view (English-only shipped; translation is post-1.0).

### MVP 1.0-alpha (explicit cut line)

The core recorder must feel excellent before breadth. **MVP =**
- Monitor capture + region capture (window capture if it falls out of WGC work naturally)
- H.264 → MP4 and MKV (NVENC/AMF/QSV when present, x264 always)
- System audio + microphone (mixed, per-source volume/mute/meters)
- Start/stop/pause/resume with countdown; screenshots (PNG + clipboard)
- Basic library index (list recordings/screenshots, open, open folder, delete)
- Tray icon + global hotkeys (F9/F10/F11)
- Light/dark theme, settings persistence, Record screen matching the design's structure (not yet pixel-perfect everywhere)
- Ships as a **portable zip** (self-contained folder, ffmpeg included)

**Explicitly deferred past MVP:** per-app audio, webcam (source + overlay), annotation/click effects, scheduler, full AV1/HEVC validation matrix, MOV/WebM, compact launcher + topbar layout, share-sheet polish, auto-update, design-perfect Library/Schedule/Settings screens.

### OS support matrix

**Windows 11 is the primary target; Windows 10 must work.** Floor: **Windows 10 2004 (build 19041) x64** — set by APIs we require anyway (process loopback audio, `WDA_EXCLUDEFROMCAPTURE`). TFM: `net10.0-windows10.0.19041.0`.

| Capability | Win 11 | Win 10 (2004+) | Handling |
|---|---|---|---|
| WGC monitor/window capture | ✔ | ✔ | — |
| WGC border suppression (`IsBorderRequired=false`) | ✔ | ✖ (yellow border shows) | Feature-gate; note in UI once |
| WGC cursor toggle | ✔ | ✔ | — |
| Mica / acrylic backdrops | ✔ | ✖ | Fallback: opaque themed background (design's solid fills) |
| Immersive dark title bar | ✔ | ✔ (19041+) | — |
| Per-app (process loopback) audio | ✔ | ✔ (2004+) | Capability-check at runtime |
| Toolbar excluded from capture | ✔ | ✔ (2004+) | — |
| Windows Share sheet | ✔ | ✔ | — |

All version-dependent paths go through one `OsCapabilities` service (checked once at startup) — no scattered version sniffing.

---

## 2. Design-file → app mapping

The design (`RecMode.dc.html`) defines exactly these surfaces — each becomes a WPF view:

| Design surface | WPF view | Notes |
|---|---|---|
| Title bar (48px, status pill, layout cycle, theme toggle, caption buttons) | `ShellWindow` custom chrome | `WindowChrome` + DWM Mica (Win11); close hover = `#C42B1C` |
| Sidebar nav (204px, accent pill) / top-tab nav | `ShellWindow` templates | Layout is a user setting cycled by title-bar button |
| Record screen | `RecordView` | Source picker (4 tiles) · context row (display/window/cam combo, region presets) · live preview · command bar · right panel (326px): Video card, Audio mixer card, Webcam card |
| Library screen | `LibraryView` | Videos/Screenshots pivot, card grid ≥236px, share flyout |
| Schedule screen | `ScheduleView` | Settings-card rows, armed state, New schedule dialog |
| Settings screen | `SettingsView` | Grouped settings cards (Appearance / Encoding defaults / Output / Recording / Hotkeys / General) |
| Compact launcher | `CompactWindow` | 392px card: source tiles, 2 quick audio rows, Record/Screenshot, summary line |
| Recording overlay | `RecordingToolbarWindow`, `CaptureBorderWindow`, `AnnotationWindow`, `CountdownWindow` | Toolbar: rec dot + timer + pause + mic + screenshot + draw + live stats + Stop; excluded from capture |
| Toast | In-shell snackbar + Windows toast when minimized | "Saved RecMode 2026-07-03 14-22-05.mp4 (864 MB)" + *Open library* |

**Design tokens → WPF resources.** Port `_ds/tokens/*.css` into `Themes/Tokens.Light.xaml`, `Tokens.Dark.xaml`, `Accent.xaml` (blue `#0078D4`, red `#D13438`, purple `#8B6CCB`, teal `#0F7B7B`, orange `#CA5010`, ramp derived like CSS `--accent-base`). Rules that must survive the port:
- 4px spacing grid; 32px controls (24px small); 48px title bar; 36px rows.
- Radius: 4px controls, 8px cards/flyouts, pill switches.
- Hairline dividers (`--stroke-divider`) instead of shadows; cards = 1px stroke + whisper shadow.
- Segoe UI Variable type ramp (Caption 12 / Body 14 / BodyStrong 14·600 / Subtitle 20·600); Cascadia Code/Consolas for numeric readouts (timer, bitrate, region px, hotkey chips).
- Fluent System Icons (vendored in `_ds/assets/icons`) → `PathGeometry`/`DrawingImage` resources with Foreground-driven color.
- Selection = left accent pill (3px) + subtle fill, never full-bleed accent.
- Sentence case everywhere; `×` in resolutions; ellipsis for in-progress status; no emoji.

---

## 3. Architecture & technology decisions

### 3.1 Solution layout
```
RecMode.sln
Directory.Build.props            # net10.0-windows10.0.19041.0, nullable, analyzers
src/
  RecMode.App/                   # WPF exe: views, view models, theming, windows
  RecMode.Core/                  # models, settings, service interfaces, state machine, error taxonomy
  RecMode.Capture/               # Windows.Graphics.Capture + D3D11 (Vortice)
  RecMode.Audio/                 # WASAPI loopback/mic/process-loopback, mixer, meters
  RecMode.Encoding/              # FFmpeg pipeline, encoder detection, muxing, screenshots
  RecMode.Interop/               # Win32/COM: hotkeys, DWM, share, recycle bin, thumbnails, OsCapabilities
tests/
  RecMode.Core.Tests/            # state machine, settings, patterns, validation matrix
  RecMode.Encoding.Tests/        # arg snapshots, integration (ffprobe-validated outputs)
  RecMode.Recording.Tests/       # recording service against fake frame/audio sources
tools/
  ffmpeg/                        # pinned ffmpeg (see §3.4)
  soak/                          # long-run soak + drift measurement scripts
```

### 3.2 Key choices

| Concern | Choice | Rationale / alternative |
|---|---|---|
| MVVM | **CommunityToolkit.Mvvm** | Source-generated observables/commands; de-facto standard |
| DI / hosting | **Microsoft.Extensions.Hosting** | Services singletons, VMs transient |
| Fluent UI | **.NET Fluent theme (`ThemeMode`) + custom styles from design tokens** | The design system is custom enough that we own the styles; WPF-UI only if it demonstrably saves time |
| Window backdrop | DWM Mica + immersive dark title bar (Win11); opaque fallback (Win10) | Matches `.mr-mica`; acrylic for flyouts/toolbar |
| Screen/window capture | **Windows.Graphics.Capture** via CsWinRT + **Vortice.Windows** (D3D11/DXGI) | GPU frames, monitor & window, cursor toggle; DXGI Desktop Duplication only if "all displays" stitching demands it |
| Region capture | WGC monitor capture + GPU crop (`CopySubresourceRegion`) | Cheap |
| Preview | D3D11 shared texture → `D3DImage` (D3D9Ex interop); fallback throttled `WriteableBitmap` | Zero-copy preview; fallback keeps feature alive |
| Encoding | **ffmpeg.exe subprocess**, frames + PCM over named pipes (see §3.3 for the two-tier strategy) | One path covers the whole codec×backend×container matrix; FFmpeg.AutoGen in-proc is the escalation |
| Hardware detection | DXGI adapter vendor IDs + `ffmpeg -encoders` + 0.2s trial-encode per candidate at startup | Combo lists only working encoders; drives "NVIDIA NVENC detected" badge |
| Audio capture | **NAudio** (WASAPI loopback + capture) + raw interop for **process loopback** (`ActivateAudioInterfaceAsync`, `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`) | Per-app audio is the differentiator; mixer at 48 kHz stereo f32 |
| Webcam | Media Foundation `SourceReader` | Frames as D3D11 textures; overlay quad or sole source |
| Global hotkeys | `RegisterHotKey` on message-only window | Remappable; conflicts surfaced |
| Tray | **H.NotifyIcon.Wpf** | Schedule runs headless from tray |
| Share | `DataTransferManager` interop + Copy path / Email / Open folder items per design flyout | |
| Delete | `IFileOperation` with `FOF_ALLOWUNDO` | Design toast: "Moved to Recycle Bin" |
| Thumbnails | `IShellItemImageFactory` + ffmpeg frame-grab fallback | |
| Settings | JSON, debounced, **versioned schema with migration tests**; location via `AppPaths` service — `.\Data\settings.json` in portable mode, `%APPDATA%\RecMode` in installed mode (§3.5) | |
| Packaging & updates | **Portable zip first** (self-contained win-x64 publish); Velopack installer + auto-update later, sharing the same build (§3.5) | |
| Logging | Serilog → `.\Data\logs` (portable) / `%LOCALAPPDATA%\RecMode\logs` (installed) | |

### 3.3 Encoder strategy — two tiers + decision gate

The "low CPU / zero-copy" goal and "raw frames over named pipes" are in tension: readback is GPU→CPU by definition. Resolved as an explicit two-tier strategy:

- **Tier 1 (MVP path):** GPU **NV12 conversion first** (video processor / compute shader), then staging-texture readback → named pipe → ffmpeg. NV12 halves+ the bytes vs BGRA (1440p60 ≈ 330 MB/s vs 885 MB/s; 4K60 ≈ 745 MB/s). Accepted CPU cost, **measured, not assumed**.
- **Tier 2 (performance path, only if gate fails):** minimize/eliminate readback — FFmpeg.AutoGen in-process with `d3d11va` hw frames handed to NVENC/AMF/QSV, or direct Media Foundation hw encoder for the H.264/HEVC subset.

**Decision gate (evaluated at end of Phase 0.5, re-checked end of Phase 3):**
Tier 1 passes if, on the dev machine at 1440p60 NVENC: sustained 0 dropped frames over 5 min, total app CPU < ~15% of one core-equivalent beyond ffmpeg itself, and pipe writes never block > 1 frame interval. 4K120 is *not* an MVP gate — if Tier 1 can't do it, that's the trigger to schedule Tier 2 work post-MVP, not to block.

Encoder matrix (what the encoder ComboBox can offer):

| Codec | NVIDIA | AMD | Intel | Software |
|---|---|---|---|---|
| H.264 | `h264_nvenc` | `h264_amf` | `h264_qsv` | `libx264` |
| HEVC | `hevc_nvenc` | `hevc_amf` | `hevc_qsv` | `libx265` |
| AV1 | `av1_nvenc` (RTX 40+) | `av1_amf` (RX 7000+) | `av1_qsv` (Arc+) | `libsvtav1` |

Probing is generic from day one (cheap, shapes the args builder); **validation rigor is H.264-first** until MVP ships, then the full matrix.

**Encoder selection rationale & currency** (why these are the best free/open choices, and how they stay that way):
- x264 and x265 are the undisputed reference open encoders for their codecs; SVT-AV1 is the only open AV1 encoder fast enough for real-time recording at high quality (libaom is better-but-unusably-slow; rav1e lags) and mainline 3.x absorbed the community PSY perceptual work, so recent versions are also the newest-featured.
- **Screen-content coding:** AV1 has tools built for exactly our content — palette mode + intra block copy. The args builder enables SVT-AV1 **screen-content mode** for screen/window/region sources (big text/UI sharpness win at the same bitrate) and leaves it off for webcam sources; hw AV1 encoders get it where their SDKs expose it.
- **Audio:** best-in-class AAC (`libfdk_aac`) is license-incompatible with redistributable builds, so bundled AAC is ffmpeg's native encoder — good at ≥128 kbps, not best-in-class. **Opus is first-class** (best open audio codec, better than AAC at every bitrate): default for MKV and WebM; AAC stays default for MP4/MOV compatibility; FLAC for lossless. (Opus entries are a logged deviation from the design's audio list → `DESIGN_NOTES.md`.)
- **Currency policy:** pin the latest *stable* ffmpeg (7.x+) at each release and **re-pin at least quarterly** so SVT-AV1/NVENC/AMF/QSV improvements flow in; the arg snapshot tests + ffprobe integration suite catch behavioral drift on every re-pin.
- Deliberately excluded: VVC/H.266 (no playback ecosystem) and VP9 (superseded by AV1 — WebM output uses AV1+Opus); the near-lossless/archival profile (backlog §7) will use x264 lossless or FFV1.

Container/codec constraints enforced in the VM (invalid combos disabled with tooltip):
- **WebM** → AV1/VP9 + **Opus** only (audio label swaps automatically).
- **MOV** → H.264/HEVC + AAC. **FLAC** → prefer MKV.
- **MKV** = crash-safest; **MP4** finalized with `+faststart`.

Quality slider → per-encoder rate control: `-crf` (x264/x265/svt-av1), NVENC `-rc vbr -cq`, AMF `-rc qvbr`, QSV `-global_quality`. Live estimate reuses the design's model: `Mbps ≈ (3 + q·0.38) · encoderFactor · fpsFactor · resFactor`.

**Encoding resource controls (user-tunable within recommended bounds).** A "Performance" group (advanced expander on the Video card + Settings section) exposes how much of the machine encoding may use — bounds computed from detected hardware, defaults = the recommendation:

| Control | Applies to | Maps to | Recommended bounds |
|---|---|---|---|
| CPU threads | Software encoders (x264/x265/SVT-AV1) | `-threads N` / `svtav1-params lp=N` | 1 … physicalCores; default `max(2, physicalCores − 2)` so capture/UI keep headroom |
| Encoder effort (speed ↔ quality/CPU) | Software | `-preset ultrafast…slow` (x264/x265), SVT-AV1 `-preset 12…6` | Slider snapped to safe presets; live "CPU cost" hint |
| GPU encoder effort | NVENC / AMF / QSV | NVENC `-preset p1…p7`, AMF `-quality speed/balanced/quality`, QSV `-preset` | Default p4/balanced; hint that higher tiers raise GPU load & latency |
| Encoder priority | ffmpeg process | Below-normal process priority toggle | On by default when recording games/full-screen |

Honesty note: GPU usage can't be hard-capped percentage-wise — the dedicated NVENC/AMF/QSV blocks barely touch 3D load anyway; the effort tier is the real, truthful lever and the UI copy says so. Recommendations come from the startup hardware probe (core count, GPU vendor/generation) and are re-validated by the trial encode.

**Output timing policy: CFR.** WGC delivers frames only when screen content changes, but editors and players want a constant frame rate. The pipeline paces output to CFR — duplicate the last frame on gaps, drop on bursts, timed against the shared QPC master clock. VFR output is a post-1.0 advanced option (§7), not a 1.0 setting.

**HDR displays.** On HDR monitors WGC produces FP16 scRGB frames; feeding them to an encoder naively yields the classic washed-out gray recording. Policy: detect HDR per captured monitor and **tone-map to SDR in the same GPU pass as the NV12 conversion** (correct transfer/gamut, no extra copy). True HDR recording (10-bit HEVC/AV1 + HDR10 metadata) is post-1.0 backlog (§7).

### 3.4 FFmpeg distribution & licensing (product decision)

FFmpeg stays an **external process/binary** — never linked into the app — which keeps RecMode's own code unencumbered. Because RecMode is **portable-first**, the decision is:

1. **Default: bundled** pinned full **GPL** build (BtbN/gyan.dev) inside the portable folder at `.\ffmpeg\ffmpeg.exe` (+ `ffprobe.exe`), SHA-256 recorded at build time and verified at startup. License notices + source link shipped in `.\licenses\` and shown in About.
2. **Setting: user-provided ffmpeg path** (validated by `-encoders` probe) for users who bring their own.
3. **Slim variant (optional later):** a small zip without ffmpeg that offers a first-run download into the app folder — same pinning, same paths.
4. **Fallback mode documented:** LGPL build (hardware encoders + svt-av1, no x264/x265) if distribution constraints ever require it — the encoder-probe architecture makes missing encoders a non-event.

### 3.5 Portable deployment model

**Portable is the primary (initial) distribution:** one zip → one folder → run `RecMode.exe`. Rules:

- **Publish:** self-contained win-x64 (`PublishSelfContained`, trimming evaluated later) — no .NET runtime install required on the host.
- **Folder layout:** `RecMode.exe` + runtime files, `.\ffmpeg\`, `.\Data\` (settings.json, library index, logs, crash-recovery temp), `.\licenses\`, `portable.marker`.
- **Mode detection:** an `AppPaths` service checks for `portable.marker` next to the exe → all state lives under `.\Data\`; if absent (future installed build) → `%APPDATA%`/`%LOCALAPPDATA%`. **No other code path may compose state paths itself.**
- **Recordings default:** `.\Recordings` (and `.\Recordings\Screenshots`) in portable mode — keeps everything in the folder, USB-friendly; user can point it at `Videos\RecMode` in Settings. Free-space guard watches the *target volume*.
- **No-install honesty:** portable mode writes **nothing** outside its folder, with two user-opt-in exceptions, each labeled in UI: "Start with Windows" (HKCU Run key pointing at the current exe path — warned that moving the folder breaks it) and Explorer thumbnail cache (OS-side, unavoidable).
- **Read-only location guard:** if `.\Data` isn't writable (Program Files, zip not extracted), a `BlockingError` explains and offers to relocate data.
- **Updates in portable mode:** manual zip replace (settings survive in `.\Data`); "check for updates on launch" just notifies + links. Velopack auto-update arrives only with the later installer build — same binaries, different `AppPaths` mode.
- **CLI + single instance:** `RecMode.exe --record [--display N | --window <match> | --region x,y,w,h] [--duration s]`, `--screenshot`, `--stop`, `--tray`. A second launch forwards its arguments to the running instance (named-pipe IPC) instead of starting twice. This makes portable automation, "start with Windows", scheduler wake-launch, and soak scripting all use one front door.

### 3.6 Error taxonomy (in `RecMode.Core` from Phase 0)

Every failure in the system maps to one of four severities, each with a defined UX channel:

| Severity | Meaning | UX | Examples |
|---|---|---|---|
| `RecoverableWarning` | Continue normally | Toast / InfoBar, auto-dismiss | Hotkey registration failed (in use); update check failed; thumbnail failed |
| `BlockingError` | Can't start the requested action | InfoBar with explanation + suggested fix | Encoder init failed after fallback chain; output folder missing/unwritable; no capture permission |
| `DegradedState` | Mid-recording, recording continues with reduced capability | Toolbar badge + toast; logged marker | Mic unplugged (source dropped, others continue); webcam lost; hw encoder reset → software fallback mid-session |
| `FatalFinalizationError` | Recorded data at risk | Recovery dialog; never silently lose data | ffmpeg crashed (attempt MKV remux/recovery); disk full (auto-stop + finalize what exists) |

Fallback chain for encoder init: selected → same codec other backend → `h264_nvenc/amf/qsv` → `libx264`, each step reported. Orphaned-recording detection on launch offers recovery for MKV. Disk-space guard: warn < 2 GB free, auto-stop-and-finalize < 500 MB.

**App-level crash safety:** a global unhandled-exception/`ProcessExit` handler flushes logs, writes a minimal crash marker, and marks the session so the next launch offers recovery. Full minidumps are opt-in/configurable and stored under `.\Data\logs\crash\` when enabled, with an optional user-initiated "share this dump" path only — see privacy stance in §1.

**Black-frame watchdog:** WGC cannot capture true exclusive-fullscreen games (borderless is fine). If captured frames stay uniformly black for a few seconds, raise a `RecoverableWarning` suggesting borderless/windowed mode instead of letting the user record minutes of nothing. DRM-protected windows also capture as black by OS design — the watchdog covers those too, and windows known to be protected warn at start.

**Pre-flight check:** starting a recording first runs instant checks — output path exists/writable, free space above threshold, selected encoder initializes, mic not silent/missing when a mic source is armed — mapping failures to `BlockingError`/`RecoverableWarning` so recordings never die at second 0 for a preventable reason.

### 3.7 Recording state machine (single source of truth in `RecMode.Core`)
```
Idle ──start──▶ Countdown(3‥1) ──▶ Recording ⇄ Paused ──stop──▶ Finalizing ──▶ Idle
        ▲ cancel ─┘                     │                        (flush, faststart, library entry, toast)
                                        └─▶ Degraded(recording) ─┘
```
Pause = stop feeding frames/samples, freeze wall-clock offset, resume shifts PTS so output has no gap. Every UI (main window, compact widget, toolbar, tray, hotkeys) drives this one service and observes its state.

### 3.8 Test strategy per subsystem

| Subsystem | Tests |
|---|---|
| State machine (`Core`) | Unit tests: every transition, cancel-during-countdown, pause PTS math, degraded transitions, double-start guarded |
| FFmpeg args (`Encoding`) | **Snapshot tests**: full arg string per (codec × backend × container × fps × quality) cell; golden files reviewed on change |
| Output validity (`Encoding` integration) | Record N seconds from a synthetic frame source → ffprobe asserts codec/container/duration/fps; runs on dev machine + CI-with-GPU when available |
| Recording service | **Fake `IFrameSource` / `IAudioSource`** (deterministic timestamps) → assert frame pacing, drop policy, A/V offset, pause gaps |
| Settings | Round-trip, defaults, **schema migration tests** (v1→vN fixtures), corrupt-file recovery |
| Filename pattern | `{date}/{time}/{source}/{codec}` expansion, illegal-char sanitization, collision suffixing |
| Encoder/container validation | Matrix unit tests: WebM+AAC rejected→Opus, MOV+AV1 disabled, FLAC→MKV steering |
| Error taxonomy | Each failure injection maps to correct severity + UX channel |
| Encoder vendor matrix | Integration suite is vendor-agnostic (runs on whatever GPU the host has); AMF/QSV entries carry a **beta label until executed on real AMD/Intel hardware**; a pre-1.0 vendor smoke checklist (record 60 s per codec, ffprobe-verify) must run on at least one NVIDIA, one AMD, one Intel machine |
| Soak (`tools/soak`) | Scripted 2 h recording; parse logs for dropped frames/drift; A/V sync via periodic beep+flash pattern, verified in output ±40 ms; **captures CPU/GPU/memory against the §3.9 budgets** |

### 3.9 Resource-efficiency principles (enforced, not aspirational)

Binding engineering rules; violations are bugs. The budget table in §1 (non-functional) is the contract; the soak harness measures it.

1. **Nothing runs that isn't visible or recording.** The WGC capture session exists only while the Record screen is visible *or* a recording is active — navigate away, minimize, or go to tray and it is torn down. WASAPI clients + level meters run only while the mixer is on screen or recording. Every timer/animation suspends on minimize/tray. Idle in tray means *zero* periodic work (the design's own `meterTick` early-out reflects this intent).
2. **Event-driven, never polled.** WGC `FrameArrived`, WASAPI event-driven buffers, `RegisterHotKey` messages, file-watcher for library. No busy loops, no sub-second polling timers anywhere.
3. **Allocation-free hot paths.** Preallocated staging-texture ring + `ArrayPool` pipe buffers; frames are reused structs; zero per-frame GC garbage — verified with an allocation profiler as part of the Phase 3 done-criteria and re-checked in soak (no Gen2/LOH churn).
4. **One copy chain on the GPU.** Capture texture → (crop/compose) → NV12 convert → single readback. No BGRA round-trips, no redundant format conversions; webcam/annotation composition happens in the same pass.
5. **UI updates are throttled and batched.** Meters ≤ 30 Hz, stats text ≤ 4 Hz, one batched Dispatcher post per tick — never per-sample/per-frame marshaling. Preview presents at ≤ 30 fps by default (setting for 60), pauses when occluded, and auto-pauses during full-screen recording (the capture border is the live indicator).
6. **Prefer the dedicated silicon.** Hardware encoders (NVENC/AMF/QSV) are the default when present — their fixed-function blocks cost near-zero CPU and negligible 3D GPU; software encoding is the explicit opt-in with the §3.3 thread cap defaulting under total cores.
7. **Frugal by default elsewhere.** Virtualized library panels; thumbnails decoded at display size and disk-cached; ffprobe metadata cached; ReadyToRun publish for fast cold start; ffmpeg at below-normal priority by default; update check only at launch, never in the background; Serilog async sink with level gating.

---

## 4. Phases

Each phase ends **green-buildable and demoable** with acceptance criteria. Risk is front-loaded: the pipeline is proven in a spike before any serious UI investment.

### Phase 0 — Foundation & scaffolding
- [ ] `git init`, `.gitignore`, `.editorconfig`, `Directory.Build.props` (net10.0-windows10.0.19041.0, nullable, x64).
- [ ] Solution + 6 src projects + 3 test projects; `Microsoft.Extensions.Hosting` in `App.xaml.cs`; CommunityToolkit.Mvvm; Serilog.
- [ ] `RecMode.Core`: settings service (JSON, versioned schema), **error taxonomy types + reporting service**, recording state machine skeleton, `OsCapabilities` service.
- [ ] FFmpeg per §3.4: bundled pinned build in `.\ffmpeg\`, startup hash verification, user-path override setting.
- [ ] `AppPaths` service + `portable.marker` mode detection (§3.5); portable self-contained publish profile + `publish-portable.ps1` producing the zip layout.
- [ ] Global crash handler + crash marker with next-launch recovery prompt; opt-in minidump writer to `.\Data\logs\crash\` (§3.6).
- [ ] Start `DOCS/DESIGN_NOTES.md`: a short log of intentional deviations from `RecMode.dc.html` (e.g. Performance group, Opus defaults, portable-first behavior, vendor-specific encoder lists, CLI) so the design file stays a trustworthy reference.
- [ ] `build.ps1` (restore/build/test); unit tests for settings + state machine skeleton.

**Done when:** `dotnet build` + `dotnet test` pass; empty themed window launches with DI'd shell VM; ffmpeg resolves on a clean machine.

### Phase 0.5 — Pipeline spike (disposable code, keep the learnings) ⚠️ decision gate
Prove the whole risky spine end-to-end before building product UI:
- [ ] WGC monitor capture → D3D11 frame acquisition at 60 fps.
- [ ] GPU NV12 convert → staging readback → named pipe → **ffmpeg h264_nvenc** (x264 fallback) → **MP4 and MKV**.
- [ ] Basic system-audio loopback (NAudio) as second pipe; mux together.
- [ ] **5-minute recording**: playable in VLC/MPC/Movies&TV, audio in sync at start *and* end (beep+flash check), zero/near-zero dropped frames, measured pipe throughput + CPU.
- [ ] Kill-ffmpeg-mid-recording test: what survives? (informs crash-safety design).
- [ ] Write up numbers → **evaluate the §3.3 decision gate**; record outcome in CLAUDE.md.

**Done when:** gate evaluated with real measurements and the spike produces learnings, not final abstractions. If Tier 1 fails at 1440p60, Tier 2 investigation happens *now*, before Phase 1.

### Phase 1 — Minimal shell + Record essentials (not the full design yet)
- [ ] Core token subset → Light/Dark ResourceDictionaries (colors, type ramp, spacing, control styles for Button/IconButton/ComboBox/Slider/ToggleSwitch/Card); runtime theme switch.
- [ ] Shell window: custom chrome, title bar, Mica (Win11)/opaque (Win10), sidebar nav only (topbar + compact deferred).
- [ ] **Record screen, functional core**: source picker tiles (screen/window/region enabled; webcam tile present-disabled), display picker, encoder/format/fps/quality controls wired to real probe results, Record/Stop button, status pill.
- [ ] Settings persistence for everything on screen; basic Settings view (theme + output folder only).
- [ ] Library/Schedule views: placeholder stubs.
- [ ] §3.9 lifecycle rules from the start: capture/preview torn down when leaving the Record screen or minimizing; no timers while idle in tray.
- [ ] All UI strings in `.resx` resource files from the first view (localization-ready; English-only shipped).

**Done when:** a user can pick a monitor, choose encoder/format/fps/quality, hit Record, and get a valid video file — through real (if not yet pixel-perfect) UI. Audio may still be spike-level or absent here; Phase 4 makes audio shippable.

### Phase 2 — Capture engine (productionize the spike)
- [ ] `RecMode.Capture` proper: WGC monitor + **window** capture (alt-tab enumeration with icons), cursor toggle, border suppression where supported.
- [ ] Frame pool → `CapturedFrame` stream; frame-rate governor (30/60/120) implementing the CFR policy (§3.3).
- [ ] **HDR handling**: per-monitor HDR detection; tone-map to SDR inside the NV12 conversion pass (§3.3) — verified against an HDR display so recordings aren't washed out.
- [ ] Black-frame watchdog (§3.6) for exclusive-fullscreen sources.
- [ ] Region source (GPU crop) + **region select overlay**: dimmed layer, drag/resize handles, px label, presets (1920×1080 / 1280×720 / Full), persisted.
- [ ] **All displays** virtual-desktop capture.
- [ ] Live preview in `RecordView` (D3DImage; WriteableBitmap fallback) showing the real source; ≤ 30 fps default, occlusion-paused (§3.9).
- [ ] Hot path allocation-free: texture ring + pooled buffers, event-driven `FrameArrived`, profiler-verified zero per-frame garbage.

**Done when:** all source types render live at target fps; region overlay matches design behavior; Record-screen-open state meets its §1 budget (< 3% CPU) at 1440p60.

### Phase 3 — Encoding pipeline (productionize + state machine) ⚠️ gate re-check
- [ ] Encoder detection service → combo entries ("AV1 · SVT-AV1", "H.264 · NVENC", …); "Hardware encoding" toggle; `hwBadge` ("GPU · NVENC" / "CPU · software").
- [ ] FFmpeg session builder with **snapshot-tested args**; filename pattern service; container constraint matrix.
- [ ] Resource controls in the args builder (§3.3): thread cap, sw/hw effort presets, encoder process priority; recommended defaults from the hardware probe (UI surfacing completes in Phase 9).
- [ ] Auto-split: optional time/size boundary via segment muxer; auto-enabled at 4 GB when the target volume is FAT32; split parts registered as one library group.
- [ ] Full state machine wiring: countdown, start/stop, **pause/resume with PTS continuity**, Degraded transitions, encoder fallback chain (§3.6).
- [ ] Live stats → status pill, expanded into a **recording health indicator**: elapsed, fps, Mbps, MB, plus dropped frames, encoder queue depth, and disk write speed (surfaces in the Phase 5 toolbar; anything unhealthy tints the stat).
- [ ] **"Safe recording" option** (on by default): record to MKV, auto-remux to MP4 (`-c copy`, seconds, no re-encode) on stop — crash safety *and* shareability without choosing.
- [ ] Crash safety: MKV recovery flow, orphan detection on launch, disk-space guard.
- [ ] Re-run decision-gate measurements with production code.

**Done when:** available H.264 combos produce valid files (others best-effort); pause leaves no gap; kill-test never corrupts app state and offers a recoverable partial MKV when possible; gate re-confirmed.

### Phase 4 — Audio core (system + mic)
- [ ] System loopback + any capture device; mixer at 48 kHz stereo f32, per-source gain + mute, soft-clip sum.
- [ ] Live RMS/peak meters driving the design's meter bars (caution color > 82%) — computed on the audio thread, UI-batched at ≤ 30 Hz; WASAPI clients torn down when mixer hidden and not recording (§3.9).
- [ ] Audio encode path: AAC 128/192/320 (MP4/MOV default), **Opus 96/128/192 (MKV/WebM default — best open codec)**, FLAC lossless; **A/V sync via shared QPC clock** with drift correction.
- [ ] Audio mixer card UI per design (minus "Add source" per-app entries — placeholder).
- [ ] Fake-source tests + soak script for sync.

**Done when:** system+mic recording mixes correctly with live meters; sync within ±40 ms over 30 min.

### Phase 5 — MVP UX layer → 🏁 **MVP 1.0-alpha**
- [ ] Global hotkeys (F9/F10/F11) + tray icon/menu + minimize-to-tray.
- [ ] Countdown overlay (3-2-1, cancellable); floating **recording toolbar** (acrylic, rec dot/timer/pause/mic-mute/screenshot/stop + live stats), excluded from capture; capture border window (red, amber when paused).
- [ ] Screenshots: frame → PNG (`Pictures\RecMode`), flash, clipboard copy, toast with *Open library*.
- [ ] Basic library: index service (ffprobe metadata cache), simple list/grid, open / open folder / delete-to-Recycle-Bin. Index records **capture-source metadata** (source type, app/window name, monitor, encoder) from day one — post-1.0 search/auto-tag depends on it existing retroactively.
- [ ] Pre-flight check wired into every start path (§3.6): UI button, hotkey, CLI, scheduler.
- [ ] Failure-mode UX pass: every §3.6 severity has its real UI (InfoBar, toolbar badge, recovery dialog).
- [ ] Portable acceptance: run from a fresh extract on another drive (and a USB stick) — record, screenshot, settings persist in `.\Data`, nothing written outside the folder.
- [ ] Idle-efficiency acceptance: 30 min parked in tray = < 0.5% CPU, zero GPU, no timer wakeups (verified with Task Manager/ETW), memory under budget.
- [ ] CLI arguments + single-instance forwarding (§3.5): `--record/--screenshot/--stop/--tray` drive the same state machine as the UI.

**Done when (= MVP acceptance):** full unattended-friendly session — hotkey start, countdown, record with toolbar, pause, screenshot mid-recording, stop, toast → library — works with the main window closed to tray, on Win11 **and** Win10, surviving a mic unplug. A forced ffmpeg kill must never corrupt app state; recovery should preserve a partial recording when the container/process state allows it and must clearly report the result to the user.

### Phase 6 — Full design fidelity
- [ ] Complete token port + all 5 accent presets + accent switching; icon library completed.
- [ ] Layouts: top-tab variant + **compact launcher** window + layout-cycle button; status pill everywhere.
- [ ] Record screen pixel pass vs design (both themes); MenuFlyout/Expander/StatusDot/snackbar styles finalized.
- [ ] Library screen per design: card grid, thumbnails, duration chip, codec badge, Videos/Screenshots pivot, share flyout (Windows Share sheet, copy path, email, open location).
- [ ] Settings screen per design: all groups laid out (controls may still be stubs where the feature comes later).
- [ ] Fluent motion: flyout/dialog fade+scale 0.96, 100–333 ms curves, toast pop; reduced-motion respected.
- [ ] Update `DOCS/DESIGN_NOTES.md` with any new visual or behavior deviations found during the full design-fidelity pass.
- [ ] Tray quick-record: tray menu lists open windows + **recent capture targets** for one-click "record this window" without opening the main window.

**Done when:** side-by-side with `RecMode.dc.html` in both themes, all layouts, the app is visually equivalent.

### Phase 7 — Full codec matrix, per-app audio, webcam
- [ ] Validate + fix the full encoder matrix: AV1/HEVC × NVENC/AMF/QSV/software × MP4/MKV/MOV/WebM (constraint matrix live in UI).
- [ ] **Per-app audio**: process-loopback sources, "Add source" session enumeration (App — Discord/Spotify/Chrome per design), capability-gated.
- [ ] (Stretch, MKV) separate track per source.
- [ ] **Webcam**: MF enumeration + camera combo; webcam-as-source; **overlay bubble** composited on GPU into the recording (bottom-right per design; draggable as enhancement); webcam card + Ctrl+Shift+W.

- [ ] Vendor-matrix smoke test on real AMD and Intel hardware (§3.8); AMF/QSV drop their beta label only after passing.

**Done when:** every advertised combo produces a valid file; per-app capture works on Win10 2004+/Win11; overlay is in the output file, toggling mid-recording works.

### Phase 8 — Annotation, mouse effects, scheduler
- [ ] Annotation ink window (captured — sits below the excluded toolbar), 4 pen colors + clear, Ctrl+Shift+D.
- [ ] Click-ripple highlighting via low-level mouse hook into the annotation layer; cursor-capture toggle surfaced.
- [ ] Scheduler: model (once/weekdays/weekly, time, duration, source, "uses current Record settings"), editor dialog, service arming timers, unattended tray start/stop, conflict guard with manual recordings; "Armed"/"Off" states per design.
- [ ] Optional: Windows scheduled task to pre-launch RecMode to tray.

**Done when:** an armed schedule records unattended from tray; drawing during recording appears in output.

### Phase 9 — Settings completion & system integration
- [ ] Filename pattern editor with live example; remappable hotkeys UI (chips + Change + conflict detection).
- [ ] **Performance settings group** (§3.3): CPU threads slider (bounded to recommended range), sw/hw encoder effort sliders with cost hints, priority toggle — advanced expander on the Video card mirrors it.
- [ ] Start with Windows (Run key, launch to tray); update check on launch (Velopack); About ("RecMode 1.0.0 · .NET 10 · WPF").
- [ ] Encoding-defaults section drives Record initial state; per-recording overrides don't clobber defaults.
- [ ] Battery/performance warning: on battery (or power-saver plan) with heavy settings, a `RecoverableWarning` suggests a lighter preset before recording starts.

**Done when:** every control on the design's Settings screen is functional and persists.

### Phase 10 — Hardening, polish, packaging
- [ ] Perf soak: 1440p60 + webcam + 3 audio sources, 2 h; memory stability; dropped-frame telemetry; **final Tier 1/Tier 2 disposition** for high-end targets (4K/120).
- [ ] Full §3.9 budget audit: every state in the §1 table measured and passing; allocation profile clean (no Gen2/LOH churn while recording); ReadyToRun publish verified.
- [ ] Multi-monitor + mixed-DPI (PerMonitorV2) correctness for overlays/toolbars; monitor unplug mid-recording.
- [ ] Accessibility: full keyboard nav, UIA names, 2px focus outlines, high-contrast, reduced motion.
- [ ] Win10 regression pass (fallback backdrop, border-visible capture, all MVP flows).
- [ ] **Portable zip is the 1.0.0 artifact**: final folder layout, license notices, hash manifest, USB smoke test; decide whether the slim (download-ffmpeg) variant ships.
- [ ] Optional/stretch: Velopack installer build (installed-mode `AppPaths`, auto-update channel); signing if cert available.
- [ ] First-run experience: encoder probe upgraded to a short **benchmark** (a few seconds per available encoder) that recommends default encoder/preset/fps for this machine, with progress UI.
- [ ] Docs: README, hotkey guide, `CHANGELOG.md` 1.0.0.

**Done when:** clean-machine (Win10 and Win11) install → record → share works; soak passes; 1.0.0 tagged.

---

## 5. Risks & mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Pipe throughput / readback cost at high res+fps | Dropped frames | **Phase 0.5 spike + explicit decision gate (§3.3)**; NV12-first; Tier 2 escalation path defined |
| WPF↔D3D11 preview interop friction | Preview perf | D3DImage path well-trodden; WriteableBitmap fallback |
| Process loopback API edge cases | Feature slip | Post-MVP (Phase 7), capability-gated; system+mic ship first |
| AV1 hw encoders only on newest GPUs | Confusion | Probe-based combo; SVT-AV1 always present; design's helper copy |
| A/V drift over long recordings | Bad output | Single QPC clock, audio-driven timestamps, soak with beep+flash verification |
| Win10 vs Win11 divergence | Bugs on Win10 | `OsCapabilities` single gate point; Win10 regression pass in Phase 10 |
| Global hotkey collisions | Annoyance | Remap UI + `RecoverableWarning` on registration failure |
| GPL ffmpeg licensing | Distribution | §3.4: external process, bundled pinned build + shipped notices, LGPL fallback mode |
| Portable mode leaks state outside folder | Broken portability promise | Single `AppPaths` chokepoint; Phase 5 portable acceptance test (fresh extract, USB) |
| HDR monitors → washed-out recordings | Silent quality bug | Per-monitor HDR detection + tone-map in the NV12 pass (§3.3); verified on an HDR display in Phase 2 |
| Exclusive-fullscreen games not capturable by WGC | User records black video | Black-frame watchdog (§3.6) warns + suggests borderless; documented limitation |
| AMF/QSV developed without AMD/Intel hardware | Broken paths ship untested | Vendor-agnostic integration suite; beta label until real-hardware smoke test passes (§3.8, Phase 7) |

## 6. Milestones

- **M1 (Phases 0–0.5):** pipeline proven — numbers in hand, gate decided.
- **M2 (Phases 1–3):** real recorder behind minimal UI — screen→MP4/MKV, hw/sw encoders, pause.
- **M3 (Phases 4–5):** 🏁 **MVP 1.0-alpha** — audio, hotkeys, tray, screenshots, basic library. **From M3 on, dogfood:** all RecMode demo/debug recordings are made with RecMode itself.
- **M4 (Phases 6–7):** the design, fully realized + full codec matrix, per-app audio, webcam.
- **M5 (Phases 8–10):** annotation, scheduler, settings, hardening → **1.0.0**.

## 7. Post-1.0 backlog (explicit non-goals for 1.0 — defend the scope)

Nothing here may creep into 1.0, but the architecture must not foreclose it — the enablers already in 1.0 are the CLI/single-instance front door, capture-source metadata in the library index, the segment-capable ffmpeg pipeline, and remux-without-re-encode.

**Top of the queue (ranked)**
1. **Replay buffer** — always keep the last 30 s / 1 min / 5 min as rolling encoded segments in `.\Data\temp`; hotkey stitches and saves retroactively. The biggest differentiator here; deserves its own phase.
2. **Lossless trim** — cut start/end from a library card via ffmpeg stream copy. Cheapest high-value item.
3. **GIF/WebP clip export** — short clips for bug reports/chat (palettegen pipeline).
4. **Profiles/presets** — **core version shipped in 1.0 (2026-07-06, pulled forward from backlog):** a Profile selector on the Record screen with the named built-in presets (Tutorial/Gameplay/Meeting/Bug report/GIF clip/High-quality archive) applying container/frame rate/quality/audio (not the encoder itself — hw availability is machine-specific), plus user-saved custom profiles (save/delete). Still backlog: **export/import**, pairing with the compact launcher and CLI, and the true near-lossless/GIF-export encodes the "GIF clip"/"High-quality archive" presets currently only approximate with existing codecs (see items 3 and the near-lossless note above). "OBS-style scenes, but simpler" is the explicit ceiling — RecMode never becomes a compositor.

**Editing suite** (grows outward from trim): convert/remux tool (MKV→MP4, H.264→HEVC/AV1), crop/resize clip export, timed blur/redact region, intro/outro and watermark.

**Annotation pro tools:** keystroke overlay, cursor magnifier, spotlight mode (dim all but cursor), numbered callouts/arrows/text labels, live zoom-to-cursor, whiteboard/pause-and-draw.

**Library pro:** search by app/window/date/codec/resolution/tags, auto-tag by source app (metadata recorded since 1.0 — Phase 5), favorites + collections, storage-cleanup assistant (huge/old/duplicate files), "show recordings from this app/window". This is the milestone where the JSON index may graduate to a single-file SQLite db in `.\Data`.

**Audio pro:** mic DSP chain (noise suppression, compressor/limiter), push-to-talk / push-to-mute, auto-duck system audio while speaking, per-source audio tracks in MKV (Phase 7 stretch lands here if cut).

**Recording behaviors:** auto-pause on idle/silence, VFR / changed-frames-only output for minimal file size (CFR stands for 1.0, §3.3 — note encoders already compress static screens to near-nothing), true HDR recording (10-bit HEVC/AV1 + HDR10 metadata; SDR tone-map ships in 1.0).

**Power-user & platform:** post-recording hooks (run command / upload / convert / move), privacy guard warning before recording sensitive windows (DRM-protected windows already warn in 1.0 via the black-frame watchdog, §3.6), translations, installer + auto-update via Velopack if not shipped as Phase 10 stretch.

*Keep `CLAUDE.md` checklist and `CHANGELOG.md` updated at every work session.*
