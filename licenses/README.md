# Third-party licenses

RecMode itself is licensed under the GNU General Public License v3.0 in the top-level `LICENSE`
file. This folder holds notices for the third-party components RecMode depends on or ships alongside, which
remain under their own separate licenses regardless of RecMode's own license. The **About** screen's License
button opens this folder directly (`AboutViewModel.OpenLicense` → `AppPaths.LicensesDirectory`).

## ffmpeg (bundled binary, staged at build/publish time — not committed to this repo)
RecMode invokes `ffmpeg.exe` as a separate process; it is not statically linked.

**Release builds** (`.github/workflows/release.yml`) download
[BtbN FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds)'s `ffmpeg-master-latest-win64-gpl.zip` — the
`-gpl` suffix is BtbN's own naming for their **GPLv3** build variant (as opposed to their separately-published
`-lgpl` variant, which RecMode does not use). Source availability for that exact build: BtbN's releases page
links each build to the exact upstream ffmpeg commit it was built from — see
https://github.com/BtbN/FFmpeg-Builds/releases and https://github.com/FFmpeg/FFmpeg for the corresponding
upstream source. Full GPLv3 text: https://www.gnu.org/licenses/gpl-3.0.txt. BtbN's own `LICENSE.txt`/
`README.txt` are now copied alongside `ffmpeg.exe`/`ffprobe.exe` into every release's `ffmpeg/` folder
(release.yml) — a prior version of this workflow's blanket `bin\*` copy silently dropped them, since they
live one directory above `bin\` in BtbN's archive.

As of this writing the workflow also **computes and ships a fresh `ffmpeg.manifest.json`** (SHA-256 of
`ffmpeg.exe`/`ffprobe.exe`, plus the exact version string `ffmpeg -version` reports for that download) next
to the binaries in every release — `FfmpegLocator` verifies it at startup and refuses to run a
build whose hash doesn't match (see `FfmpegLocator.cs`). This pins "the binary RecMode is about to run
matches what this specific release's workflow actually downloaded and staged" — it does not, and cannot,
attest to BtbN's own build process; that trust boundary is inherent to depending on a third-party
redistribution rather than building ffmpeg from source ourselves.

**Local/dev builds** (`tools/ffmpeg`, gitignored — see `README.md` → Development) are whatever the developer
staged there manually and are not covered by the above; `FfmpegLocator` reports "no manifest present" for
those (a Warning, not blocking) rather than assuming any particular license variant.

## .NET runtime (self-contained publish)
The .NET runtime and its shared framework are MIT-licensed (© .NET Foundation and Contributors). See
https://github.com/dotnet/core/blob/main/LICENSE.TXT.

## NuGet dependencies (see `Directory.Packages.props` for pinned versions)
| Package | License |
|---|---|
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.Hosting | MIT |
| Serilog, Serilog.Extensions.Hosting, Serilog.Sinks.File, Serilog.Sinks.Async | Apache-2.0 |
| Vortice.Direct3D11, Vortice.DXGI | MIT |
| NAudio | MIT |
| H.NotifyIcon.Wpf | MIT |
| Velopack | MIT |

These are notices, not full reproduced license text — see each project's repository for the complete
license. This table should be kept in sync with `Directory.Packages.props` when dependencies change.

This folder is intentionally checked in (as a placeholder for the ffmpeg specifics above) so the portable
layout has a stable location to read from once the About screen is wired up to display it.
