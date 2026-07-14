<p align="center">
  <img src="src/RecMode.App/Assets/AppIcon.ico" alt="RecMode" width="96" />
</p>

# RecMode

RecMode is a modern Windows screen recorder built with .NET 10 and WPF. It focuses on fast desktop capture, practical recording presets, hardware-accelerated encoding where available, and a clean Windows 11-style interface.

## Features

- Record a full display, a single window, a custom region, or all displays on multi-monitor systems.
- Follow selected window mode for apps that recreate their top-level window.
- Capture screenshots from the current source.
- Record system audio, microphone audio, or isolate audio to a selected app.
- Use H.264, HEVC/H.265, or AV1 where supported by the local hardware and ffmpeg build.
- Use hardware encoders when available: NVIDIA NVENC, AMD AMF, Intel QSV, or software fallback.
- Save as MP4, MKV, MOV, or WebM, with compatibility checks.
- Use built-in recording profiles such as Tutorial, Gameplay, Meeting, Bug report, GIF clip, and High-quality archive.
- Save custom recording profiles.
- Cycle recording profiles with a global hotkey.
- Pause and resume recordings.
- Add click highlights, draw annotations, and webcam picture-in-picture overlays.
- Schedule recordings and optionally bind schedules to profiles.
- Browse recordings and screenshots in the Library, including a Record again action.
- Use portable or installer-based distribution.

## Requirements

- Windows 11 recommended.
- Windows 10 version 2004 or newer supported.
- x64 Windows.
- No separate .NET runtime required for packaged builds; RecMode is published self-contained.
- Bundled ffmpeg/ffprobe are included in packaged builds.
- Optional GPU hardware encoder support depends on installed hardware and drivers.
- Optional microphone and webcam hardware for those capture modes.

## Quick Start

1. Open RecMode.
2. Choose a source on the Record page: Screen, Window, or Region.
3. Select a recording profile, or choose encoder, format, frame rate, and quality manually.
4. Choose audio sources: system audio, microphone, or a specific app.
5. Optional: enable webcam overlay, click highlights, or draw mode.
6. Press Record or use `F9`.
7. Pause/resume with `F10`.
8. Stop with `F9`.
9. Open the Library page to find the recording.

## Default Hotkeys

| Hotkey | Action |
| --- | --- |
| `F8` | Cycle to next recording profile |
| `F9` | Start / stop recording |
| `F10` | Pause / resume |
| `F11` | Screenshot |

Hotkeys are global and can be changed in Settings.

## Installation

RecMode can be distributed in two main forms:

- Portable zip: extract the folder and run `RecMode.exe`.
- Installer: run `RecMode-win-Setup.exe` or use the generated MSI package.

The one-click setup installer can be installed to a custom path from a terminal:

```powershell
RecMode-win-Setup.exe --installto "D:\Apps\RecMode"
```

The MSI supports Windows Installer deployment. Administrators can override the target folder with:

```powershell
msiexec /i RecMode-win.msi VELOPACK_INSTALLDIR="D:\Apps\RecMode"
```

## Portable Layout

Portable builds keep state beside the app:

```text
RecMode/
  RecMode.exe
  LICENSE
  portable.marker
  ffmpeg/
  licenses/
  Data/
    settings.json
    library.json
    logs/
  Recordings/
    Screenshots/
```

```

## Status

RecMode is currently beta software. Some hardware-specific verification is still dependent on access to specific NVIDIA, Intel, webcam, microphone, and Windows 10 test hardware.

Known beta limitation: full-system audio recording is tracked as an open issue in local project notes; per-app audio targeting has been separately verified.

## License

RecMode is proprietary, freeware software: you may use it free of charge, but you may not modify, redistribute, or sell it. See `LICENSE` for the full terms. Third-party notices for redistributed components are kept under `licenses/`.
