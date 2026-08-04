<p align="center">
  <img src="src/RecMode.App/Assets/AppIcon.ico" alt="RecMode" width="96" />
</p>

# RecMode

**RecMode** is a modern Windows screen recorder built with **.NET 10** and **WPF**. It targets fast desktop capture, practical recording presets, hardware-accelerated encoding where available, and a clean Windows 11-style interface. Portable-first: extract a folder or install once, and all recordings and settings stay beside the app.

[![Version](https://img.shields.io/badge/version-0.9.101%20Beta-blue)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](#requirements)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)](#requirements)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](#license)

## Highlights

| Area | What you get |
| --- | --- |
| **Capture** | Full display, single window (dropdown or click-to-pick), custom region, or all displays on multi-monitor setups |
| **Encoding** | H.264, HEVC, or AV1 via NVIDIA NVENC, AMD AMF, Intel QSV, or software fallback |
| **Containers** | MP4, MKV, MOV, WebM with compatibility checks |
| **Audio** | System loopback, microphone, or per-app isolation |
| **Overlays** | Webcam picture-in-picture, click highlights, draw-on-screen annotation |
| **Automation** | Global hotkeys, scheduled recordings, CLI flags, system-tray quick actions |
| **Library** | Browse videos and screenshots, open, reveal, delete, or **Record again** |

## Features

### Capture sources

- **Screen** — record a chosen monitor, or **All Displays** when you have two or more monitors.
- **Window** — pick from a list, use **Manual Pick** to click a window on screen, or enable **Follow selected window** for apps that recreate their window handle.
- **Region** — drag a rectangle with presets (1920×1080, 1280×720, Full); re-open the picker any time by clicking the Region tile.
- **Webcam** — record your camera directly as the capture source (separate from the picture-in-picture overlay, below).
- **Live preview** — see what will be recorded before you press Record (pauses while recording to save resources).

### Video and encoding

- Hardware encoders probed at startup (trial-encoded, not just listed), with a first-run benchmark to recommend defaults.
- **Quality** slider with perceptual mapping, per-encoder calibration, tier readout, and Web / Balanced / Archive snap points.
- **Brightness** adjustment and **HDR-to-SDR tone mapping** applied on the GPU in the capture pipeline (live in preview and during recording).
- **Smart auto-zoom** (beta) — smoothly zooms in around each click, easing back out after a few idle seconds (Screen and Region sources); manual zoom is also available.
- **Safe recording** (default on) — writes a crash-safe MKV first, then remuxes to MP4/MOV on stop.
- **Auto-split** for very large files (optional, FAT32-aware size threshold).
- **Bitrate guardrail** (default on) to cap surprise file growth on complex content.

### Audio

- System audio and microphone, each with enable toggle, volume slider, and live level meter.
- **Limit to app** — capture only one running application's audio instead of the full system mix.
- Codecs steered by container: AAC (MP4/MOV), Opus (MKV/WebM), FLAC (MKV).

### Recording profiles

Built-in presets (with tooltips describing quality and frame rate):

- Tutorial (Balanced quality, 30 fps)
- Gameplay (High quality, 60 fps)
- Meeting (Standard quality, 30 fps)
- Bug report (Small file, 30 fps)
- Quick clip (Low quality, 15 fps, no audio)
- Archive (Maximum quality, 60 fps, lossless audio)

Save your own custom profiles, delete them, cycle presets with **F8**, or bind a profile to a scheduled recording. Built-in presets can also be **edited in place** — save over a preset's own name to override it, and delete the override to revert to its shipped defaults.

### While recording

- Floating **recording toolbar** (timer, pause, screenshot, stats, stop) — excluded from the capture.
- Optional **countdown** before interactive starts.
- **Click highlights** — accent ripple at each mouse click (included in the recording).
- **Keystroke visualizer** — shows hotkey combinations like Ctrl + Z on screen as you press them.
- **Draw mode** — freehand ink over the capture area; exit with Esc, F12, or the on-screen button.
- **Webcam overlay** — corner picture-in-picture with device, position, and size controls (independent of recording the webcam as its own source).
- **Pause / resume** with gapless output timing.
- Auto-pause on session lock, and pre-flight/mid-recording warnings for a full battery, low disk space, or a disk too slow to keep up.

### Library, schedule, and settings

- **Library** — Videos and Screenshots tabs, thumbnails, metadata from `library.json`, Record again.
- **Schedule** — recurring or one-off timed recordings; optional profile binding; fires while the app runs (including from tray).
- **Settings** — appearance (theme, accent, Sidebar / Top bar / **Compact** layout), encoding defaults, output paths and filename pattern, recording toggles, remappable global hotkeys, performance controls, start with Windows, close-button behavior (exit or minimize to tray), and update check.
- **About** — version, runtime info, privacy notes, and license/third-party notices.

### Distribution and privacy

- **Portable zip** — self-contained, no `%AppData%` writes; state lives in `.\Data\`.
- **Installer** — MSI with an interactive install-folder picker; bundled ffmpeg and license notices included.
- **No telemetry** — RecMode does not phone home; update check is opt-in and can be disabled.

## Requirements

- **Windows 11** recommended; **Windows 10 version 2004 (build 19041)** or newer supported.
- **x64** Windows only.
- Packaged builds are **self-contained** — no separate .NET runtime install required.
- **ffmpeg/ffprobe** bundled in portable and installer packages.
- GPU hardware encoding depends on your GPU and drivers (NVENC / AMF / QSV).
- Microphone and webcam are optional hardware for those features.

## Quick start

1. Extract the portable zip or run the installer, then launch **RecMode.exe**.
2. On the **Record** page, choose **Screen**, **Window**, or **Region**.
3. Pick a **Profile** or set encoder, format, frame rate, and quality manually.
4. Enable **System audio**, **Microphone**, or **Limit to app** as needed.
5. Press **Record** or press **F9**.
6. Use **F10** to pause/resume and **F9** again to stop.
7. Open **Library** to play, reveal, or **Record again**.

## Default hotkeys

| Hotkey | Action |
| --- | --- |
| `F8` | Next recording profile |
| `F9` | Start / stop recording |
| `F10` | Pause / resume |
| `F11` | Screenshot |
| `Esc` / `F12` | Exit draw mode (while annotating) |

All global hotkeys can be remapped under **Settings → Hotkeys**.

## Command line

RecMode is single-instance: a second launch forwards commands to the running app.

| Flag | Action |
| --- | --- |
| `--record` / `-r` | Start recording (skips countdown) |
| `--stop` | Stop the current recording |
| `--screenshot` | Capture a still from the current source |
| `--tray` | Start minimized to the system tray |

Examples:

```powershell
RecMode.exe --tray
RecMode.exe --record
RecMode.exe --stop
RecMode.exe --screenshot
```

## Installation

### Portable

1. [Download the latest portable ZIP](https://github.com/Andyucu/RecMode/releases/latest/download/RecMode-win-Portable.zip).
2. Run `RecMode.exe` from the extracted folder.
3. Recordings default to `.\Recordings\`; settings to `.\Data\`.

### Installer

- [Download the latest MSI installer](https://github.com/Andyucu/RecMode/releases/latest/download/RecMode-win.msi), then choose the install location or deploy it through standard MSI management tools.

Custom install path:

```powershell
msiexec /i RecMode-win.msi VELOPACK_INSTALLDIR="D:\Apps\RecMode"
```

## Portable folder layout

```text
RecMode/
  RecMode.exe
  LICENSE
  portable.marker
  ffmpeg/
    ffmpeg.exe
    ffprobe.exe
  licenses/
  Data/
    settings.json
    library.json
    logs/
  Recordings/
    Screenshots/
```

## Status and known limitations

RecMode is **beta** software (`0.9.x-beta`). Some items still depend on hardware or environment we have not fully verified on every vendor:

The latest review hardening includes safe live microphone toggling, static-desktop-safe capture health checks, preview backpressure, race-safe scheduling and library indexing, keyboard region selection, locale-aware schedule validation, and improved screen-reader/live-region metadata. The local Release validation suite currently passes 648 tests with zero warnings.

- NVENC and QSV encoding on real NVIDIA / Intel hardware (development machine is AMD).
- Multi-monitor **All Displays** compositing on machines with two or more physical monitors.
- Real webcam hardware verification (synthetic test path is verified).
- A real screen-reader (Narrator) accessibility pass, and Windows 10 (build 19041) regression testing.

## License

RecMode is free software licensed under the **GNU General Public License v3.0**. See [LICENSE](LICENSE). Third-party notices for bundled components are in `licenses/`.
