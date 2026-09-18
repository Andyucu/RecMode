<p align="center">
  <img src="assets/AppIcon.png" alt="RecMode" width="96" />
</p>

# RecMode

**RecMode** is a modern Windows screen recorder built with **.NET 10** and **WPF**. It targets fast desktop capture, practical recording presets, hardware-accelerated encoding where available, and a clean Windows 11-style interface. Portable-first: extract a folder or install once, and all recordings and settings stay beside the app.

[![Version](https://img.shields.io/badge/version-0.9.157%20Beta-blue)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](#requirements)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)](#requirements)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](#license)

## Highlights

| Area | What you get |
| --- | --- |
| **Capture** | Full display, single window (dropdown or click-to-pick), custom region, or all displays on multi-monitor setups |
| **Encoding** | H.264, HEVC, or AV1 via NVIDIA NVENC, AMD AMF, Intel QSV, or software fallback |
| **Containers** | MP4, MKV, MOV, WebM with compatibility checks |
| **Audio** | System loopback, microphone, per-app isolation, **separate mic/system tracks** (on by default), microphone **noise suppression** |
| **Privacy** | **Live redaction** — mark a rectangle and it never reaches the encoder, before or *during* a recording; **no telemetry, ever** |
| **Captions** | **Offline transcripts** (Whisper, on your PC) written as `.srt`/`.vtt`, searchable by the words spoken, exportable as text or captions |
| **Navigation** | **Chapter markers** stamped while recording, written into the file as real seekable chapters |
| **Overlays** | Webcam picture-in-picture, click highlights, draw-on-screen annotation, **smooth cursor** |
| **Automation** | Global hotkeys, scheduled recordings, CLI flags, system-tray quick actions |
| **Library** | Browse videos, screenshots and **audio tracks**; open, reveal, delete, **Record again**, or save any single audio track out on its own |

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
- **Smooth cursor** (Settings → Recording) — captures with the cursor off and composites an eased, size-adjustable cursor instead, for the polished motion raw pointer sampling can't give. GPU capture path only; on the compatibility path the normal cursor is captured.
- **Smart auto-zoom** (beta) — smoothly zooms in around each click, easing back out after a few idle seconds (Screen and Region sources); manual zoom is also available.
- **Safe recording** (default on) — writes a crash-safe MKV first, then remuxes to MP4/MOV on stop.
- **Auto-split** for very large files (optional, FAT32-aware size threshold).
- **Bitrate guardrail** (default on) to cap surprise file growth on complex content.

### Audio

- System audio and microphone, each with enable toggle, volume slider, and live level meter.
- **Limit to app** — capture only one running application's audio instead of the full system mix.
- **Separate audio tracks** (MKV/MOV, **on by default**) — the mixed track is always there, plus distinct **Microphone** and **System** tracks, so levels can be rebalanced later instead of being baked into one stream. The per-source tracks are the thing you cannot recreate after the fact, which is why they are recorded unless you opt out.
- **Play or save any single track** from the Library's **Audio** tab — play it on its own, or save it out as its own file. Both are a stream copy, so both are instant and lossless, into a file extension that actually fits the codec. **Show in folder** and **Delete** act on the recording that holds the tracks, since a track is part of that file rather than a file of its own.
- **Reduce background noise** — a zero-latency high-pass + adaptive expander that drops steady hiss, fan and rumble between speech. The separate mic track stays raw, so the cleanup is never destructive.
- Codecs steered by container: AAC (MP4/MOV), Opus (MKV/WebM), FLAC (MKV).

### Privacy

- **Live redaction** — mark a rectangle (Record screen → **Privacy**, or the floating toolbar) and it is blanked out in the recorded frames; the marked area never reaches the encoder.
- **Mark it mid-recording.** The thing you need to hide often appears *after* you start — a password prompt, a customer name, a token. The picker is capture-excluded while recording, so the act of marking what to hide isn't itself recorded.
- The toolbar's redact button blanks the marked area while it is on, and **removes the mark** on a second press.
- **Frosted or black.** The panel is a soft **frosted-glass** panel by default, with plain **black** still selectable — both are fully opaque, so nothing behind either one is readable; the frost is texture, not transparency. That choice is deliberate rather than decorative: a translucent frost can leave large text legible, which is the one outcome a redaction must never have.
- **It can follow the element it hides.** With **Follow the marked element** on, the panel tracks the thing you marked as it moves and keeps covering it. Best-effort by design — it follows rigid, high-contrast things (dialogs, buttons, text blocks) and can lose anything whose pixels change as it moves, like animation or scrolling content. When it loses the element the panel **holds its last position** and you get a warning, so it never quietly stops covering.
- Starting with redaction on and **no area picked yet** is fine — it records unredacted until you mark one, which is the usual case when the thing to hide only turns up mid-recording.
- Only available where the capture can actually composite it: with an area marked that **can't** be applied, RecMode **refuses to start rather than record unredacted**.
- **Recordings never leave the machine.** The only feature that touches the network is the transcript model download, and it uploads nothing — see below.

### Transcripts and captions

- **Local speech-to-text** — the **Transcripts** page turns a recording into `.srt` + `.vtt` captions beside it, using Whisper **on this PC**. Every comparable tool uploads your recording to do this; RecMode does not.
- **Search by spoken word** — one search box finds a recording by what was said in it, showing the matching line.
- **Save as…** — write the transcript out wherever you want: plain text for pasting into a doc or bug report, or the `.srt` / `.vtt` caption file.
- **Transcribe without captions, if you prefer.** **Save subtitles with the recording** (on by default) decides whether a transcript also lands beside the video as `.srt`/`.vtt` and marks it as transcribed. Turn it off and the words still **survive and stay searchable** — they're kept in the app's own `Data/Transcripts/` folder instead — but nothing is written next to the recording, so the video itself stays unmarked.
- Captions come from the **microphone track** when the recording has one (the mix also carries system audio, which degrades recognition), falling back to the first audio stream otherwise.
- The speech model is a **one-time, opt-in download** (75–466 MB, size shown before anything is fetched) because it's far too large to bundle in a portable zip. **Remove model** deletes it again and reclaims the space when you're done with it. Nothing else about transcription touches the network.

### Chapters

- Stamp a **chapter** during recording with the toolbar button or **Ctrl+Shift+K**. Chapters are written into the file as **real, seekable chapters** in both MP4 and MOV/MKV — no re-encode, and they ride along with the safe-recording remux.
- Chapter titles are listed in the **Library**, and work in any player.

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

- Floating **recording toolbar** (timer, pause, screenshot, stats, stop) — excluded from the capture. It also carries the mid-recording toggles: **mic mute**, **reduce background noise**, **redact area**, and **add chapter**.
- Optional **countdown** before interactive starts.
- **Click highlights** — accent ripple at each mouse click (included in the recording).
- **Keystroke visualizer** — shows hotkey combinations like Ctrl + Z on screen as you press them.
- **Draw mode** — freehand ink over the capture area; exit with Esc, F12, or the on-screen button.
- **Webcam overlay** — corner picture-in-picture with device, position, and size controls (independent of recording the webcam as its own source).
- **Pause / resume** with gapless output timing.
- Auto-pause on session lock, and pre-flight/mid-recording warnings for a full battery, low disk space, or a disk too slow to keep up.

### Library, schedule, and settings

- **Library** — **Videos**, **Screenshots** and **Audio** tabs, thumbnails, metadata from `library.json`, Record again, and chapter titles for recordings that have them. The Audio tab lists each recording's separate tracks with per-track **Play** and **Save as…**, plus **Show in folder** and **Delete** for the recording itself.
- **Deleting always asks first** — a recording, a screenshot or a downloaded model alike — and the prompt says whether it goes to the Recycle Bin or can't be undone.
- **Transcripts** — its own page: transcribe a recording locally, then search every transcript by the words spoken.
- **Schedule** — recurring or one-off timed recordings; optional profile binding; fires while the app runs (including from tray).
- **Settings** — appearance (theme, accent, Sidebar / Top bar / **Compact** layout), encoding defaults, output paths and filename pattern, recording toggles (including **smooth cursor** and **redaction**), remappable global hotkeys, performance controls, start with Windows, close-button behavior (exit or minimize to tray), and update check.
- **About** — version, runtime info, privacy notes, and license/third-party notices.

### Distribution and privacy

- **Portable zip** — self-contained, no `%AppData%` writes; state lives in `.\Data\`.
- **Installer** — MSI with an interactive install-folder picker; bundled ffmpeg and license notices included.
- **No telemetry** — RecMode collects nothing and sends nothing about you or your recordings. The only outbound requests are the update check against the GitHub releases feed (on by default, switchable off in Settings) and the one-time speech-model download if you choose to use transcripts; nothing is ever uploaded.

## Requirements

- **Windows 11** recommended; **Windows 10 version 2004 (build 19041)** or newer supported.
- **x64** Windows only.
- Packaged builds are **self-contained** — no separate .NET runtime install required.
- **ffmpeg/ffprobe** bundled in portable and installer packages.
- GPU hardware encoding depends on your GPU and drivers (NVENC / AMF / QSV).
- Microphone and webcam are optional hardware for those features.
- **Transcripts** need a one-time, opt-in speech-model download (75–466 MB) and run on CPU; a larger model is slower but more accurate.

## Quick start

1. Extract the portable zip or run the installer, then launch **RecMode.exe**.
2. On the **Record** page, choose **Screen**, **Window**, or **Region**.
3. Pick a **Profile** or set encoder, format, frame rate, and quality manually.
4. Enable **System audio**, **Microphone**, or **Limit to app** as needed.
5. Press **Record** or press **F9**.
6. Use **F10** to pause/resume and **F9** again to stop.
7. Open **Library** to play, reveal, or **Record again** — or **Transcripts** to caption a recording locally and search what was said.

Optional, before recording: mark a **redact area** (Record → Privacy) to blank something out, turn on **separate audio tracks** to keep mic and system audio independently editable, or enable **smooth cursor** in Settings.

## Default hotkeys

| Hotkey | Action |
| --- | --- |
| `F8` | Next recording profile |
| `F9` | Start / stop recording |
| `F10` | Pause / resume |
| `F11` | Screenshot |
| `Ctrl+Shift+M` | Mute / unmute the microphone |
| `Ctrl+Shift+K` | Add a chapter marker (while recording) |
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
    models/            <- opt-in speech model for transcripts (created on download)
  Recordings/
    Screenshots/
    <name>.srt         <- captions, written beside the recording they belong to
    <name>.vtt
```

Everything stays inside this folder: settings, library metadata, logs, the downloaded speech model, recordings, screenshots and captions. Nothing is written to `%AppData%` or `%Videos%`.

## Status and known limitations

RecMode is **beta** software (`0.9.x-beta`). Some items still depend on hardware or environment we have not fully verified on every vendor.

## License

RecMode is free software licensed under the **GNU General Public License v3.0**. See [LICENSE](LICENSE). Third-party notices for bundled components are in `licenses/`.
