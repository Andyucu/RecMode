# ffmpeg (bundled at runtime)

RecMode runs ffmpeg as an **external process** (never linked) — see IMPLEMENTATION_PLAN.md §3.4.

## What ships here in a release
At runtime the app resolves ffmpeg from **`.\ffmpeg\ffmpeg.exe`** (+ `ffprobe.exe`) next to `RecMode.exe`,
or from a user-provided path set in Settings. Binaries are **not committed to git** (large, and licensing
notices ship alongside them). `publish-portable.ps1` copies them from `tools\ffmpeg\` into the portable
folder.

## Pinned build (default: full GPL)
- Source: gyan.dev / BtbN "full" GPL build, ffmpeg 7.x+.
- After staging the binaries under `tools\ffmpeg\`, generate `ffmpeg.manifest.json` with the SHA-256 of
  each exe so the app can verify integrity at startup (see `FfmpegManifest` / `FfmpegLocator`):

```jsonc
{
  "build": "gyan.dev 7.1 full (GPL)",
  "ffmpegSha256": "<sha256 of ffmpeg.exe, lowercase hex>",
  "ffprobeSha256": "<sha256 of ffprobe.exe, lowercase hex>"
}
```

Compute hashes with:  `Get-FileHash tools\ffmpeg\ffmpeg.exe -Algorithm SHA256`

## Licensing
Because the default build is GPL, license notices + a source-code link ship in `.\licenses\` and are shown
in About. An LGPL fallback build (hardware encoders + SVT-AV1, no x264/x265) is documented as an option if
distribution constraints ever require it — the encoder-probe architecture makes missing encoders a non-event.
