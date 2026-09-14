# Music Player Plugin (`frypdf.overlay.musicplayer`)

Floating playlist-based music player with playback controls, seek/volume, and real ID3 metadata (title/artist/album/cover art), built with [SoundFlow](https://github.com/LSXPrime/SoundFlow) (a MiniAudio-backed .NET audio engine) + TagLibSharp.

## Features
- **Automatic M3 Expressive Chrome**: Docks into the `shell.overlay` slot with drag and pin physics.
- **Playlist**: Add local WAV/MP3/FLAC files via a file picker; playlist persists between sessions.
- **Real Metadata**: Title, artist, album, and embedded cover art are read from each file's tags via TagLibSharp.
- **Full Transport Controls**: Play/pause, next/previous, stop, seek bar, volume, and Off/Repeat-All/Repeat-One modes.
- **Persisted Settings**: Volume, repeat mode, playlist, and last-played track are saved through the plugin settings store.
- **Genuinely cross-platform playback**: SoundFlow ships proper `runtimes/<rid>/native/` MiniAudio binaries for Windows, macOS (Intel *and* Apple Silicon), and Linux, so the `.fryplugin` bundles one native tree that works everywhere — no external app install required.

## Why SoundFlow instead of LibVLCSharp
LibVLCSharp was the initial choice, but two real, verified blockers ruled it out:
1. The `VideoLAN.LibVLC.Mac` NuGet package (v3.1.3.1) ships only an **x86_64** `libvlc.dylib` — it does not run on Apple Silicon at all (confirmed via a native architecture-mismatch crash).
2. Even on Intel, that package is just a bare `libvlc.dylib` with no `libvlccore.dylib` and no `plugins/` folder, so it can't actually decode or output audio without a full VLC.app installed separately as an external dependency — not acceptable for a self-contained plugin.

SoundFlow was verified directly (NuGet package contents inspected, and a standalone smoke test played both a WAV and an MP3 file with accurate position/duration tracking and end-of-track detection) to ship working arm64 *and* x64 macOS native binaries with no external app dependency.

## Known Limitations
- **Format support is WAV/MP3/FLAC only** for this build (SoundFlow's built-in MiniAudio decoders). Broader format support (OGG, M4A, AAC, ...) would need the optional `SoundFlow.Codecs.FFMpeg` extension package, not included here.
- **SoundFlow's maintainer announced a hiatus (Jan 2026 – Feb 2027)** at the time this plugin was built — the current 1.4.1 release works standalone (verified), but don't expect upstream fixes/updates during that window.

## Project Structure
```
MusicPlayerPlugin/
├── MusicPlayerPlugin.cs            # IFryPlugin implementation
├── MusicPlayerViewModel.cs         # Playback engine (SoundFlow/MiniAudio) + playlist/settings state
├── TrackViewModel.cs               # Per-track metadata (TagLibSharp)
├── RepeatModeIconConverter.cs
├── MusicPlayerView.axaml / .axaml.cs
├── plugin.json
└── Runner/                         # Standalone Avalonia host for local dev/testing
```

## Building and Packaging
To build and create the `.fryplugin` distribution archive:
```bash
dotnet build -c Release
```
This produces `bin/Release/net10.0/MusicPlayer.fryplugin`, bundling `MusicPlayerPlugin.dll`, `SoundFlow.dll`, `TagLibSharp.dll`, `plugin.json`, and SoundFlow's native MiniAudio runtime tree (not provided by the host, unlike Avalonia/CommunityToolkit.Mvvm/Material.Icons.Avalonia).

## Local Testing
```bash
dotnet run --project Runner/MusicPlayerPlugin.Runner.csproj
```
Launches a standalone window hosting the player without needing a FryPDF install.

## Installing into FryPDF
Same three options as the other example plugins — drag-and-drop the `.fryplugin` onto the FryPDF window, use the Command Palette's "Install Plugin" action, or drop it into the auto-discovery plugins folder (`~/Library/Application Support/FryPdf/plugins/frypdf.overlay.musicplayer/` on macOS).
