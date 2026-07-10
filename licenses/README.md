# Third-party licenses

RecMode itself is licensed under the proprietary terms in the top-level `LICENSE` file (free to use,
no modification, no redistribution, no resale). This folder holds notices for the third-party components
RecMode depends on or ships alongside, which remain under their own separate licenses regardless of
RecMode's own license. Note: as of this writing the **About** screen does not yet read from this folder —
wiring that up is tracked as a follow-up (see `README.md`).

## ffmpeg (bundled binary, staged at build/publish time — not committed to this repo)
RecMode invokes `ffmpeg.exe` as a separate process; it is not statically linked. Depending on the exact
build staged into `tools/ffmpeg` (see `README.md` → Development), ffmpeg is licensed **GPLv2/GPLv3 or
LGPLv2.1/LGPLv3**. Whoever pins the ffmpeg build for a release **must**:
1. Record the exact ffmpeg version/build here (e.g. "ffmpeg 7.x, gyan.dev `full_build`, GPLv3").
2. Include (or link to) the corresponding source for that exact build, per GPL/LGPL source-availability
   requirements — a link to the build page and the upstream ffmpeg source tag/commit is sufficient.
3. Include the matching license text (`GPL-2.0`, `GPL-3.0`, `LGPL-2.1`, or `LGPL-3.0` from
   https://www.gnu.org/licenses/).
This is currently a placeholder — no ffmpeg build/source URL has been pinned in this repo yet.

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
