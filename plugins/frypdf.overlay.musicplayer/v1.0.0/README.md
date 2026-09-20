# MusicPlayerPlayground

[![CI](https://github.com/PrashantUnity/MusicPlayerPlayground/actions/workflows/ci.yml/badge.svg)](https://github.com/PrashantUnity/MusicPlayerPlayground/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-12.1.2-red.svg)](https://avaloniaui.net/)

**MusicPlayerPlayground** is an interactive, floating music player overlay and standalone desktop audio player for .NET 10 and Avalonia UI. It features an authentic Material Design 3 (M3) Expressive visual design, tactile transport controls, real-time audio visualization, queue management, and genuine cross-platform hardware playback powered by [SoundFlow](https://github.com/LSXPrime/SoundFlow) (MiniAudio) and [TagLibSharp](https://github.com/mono/taglib-sharp).

It operates both as:
1. **A Standalone Desktop Application**: Direct F5 executable with dark theme, hot reload, and local playlist state.
2. **A FryPDF Ecosystem Plugin (`frypdf.overlay.musicplayer`)**: Floating draggable and pinnable overlay widget for FryPDF.

---

## 🌟 Key Features

- **Material Design 3 Expressive UI**:
  - Custom draggable window chrome with pill drag handle, title, minimize/maximize, and close actions.
  - Interactive tactile transport deck with smooth animations and dynamic album art blurring backdrop.
- **Cross-Platform MiniAudio Engine**:
  - Genuinely self-contained native audio decoders for Windows (`win-x64`), macOS (`osx-arm64` & `osx-x64`), and Linux (`linux-x64`).
  - Supports WAV, MP3, and FLAC playback without external system runtime dependencies.
- **Rich Metadata & Tag Extraction**:
  - TagLibSharp ID3/Vorbis reader extracts title, artist, album, duration, track number, and embedded cover art.
  - Memory-safe bitmap decoding (bounded to 400px width) prevents large object heap (LOH) fragmentation.
- **Queue & Playlist Management**:
  - Instant file picker and drag-and-drop support.
  - Multi-mode repeat (Off, Repeat-One, Repeat-All) with dynamic Material icon transitions.
  - Interactive search and filter within active queue.
  - Track favoriting and quick reveal in file manager.
- **Waveform & Tactile Slider**:
  - Custom `WavySlider` control with fluid wave rendering during active playback.

---

## 🚀 Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Run Standalone Desktop App
```bash
# Clone the repository
git clone https://github.com/PrashantUnity/MusicPlayerPlayground.git
cd MusicPlayerPlayground

# Run standalone desktop app
dotnet run --project Runner/MusicPlayerPlugin.Runner.csproj
```

### Run Automated Unit Tests
```bash
dotnet test MusicPlayerPlugin.slnx
```

---

## 📦 Solution Structure

```
MusicPlayerPlayground/
├── MusicPlayerPlugin.slnx          # Modern XML solution (Plugin + Runner + Tests)
├── MusicPlayerPlugin.csproj        # Dual-mode .NET 10 plugin & packaging target
├── MusicPlayerPlugin.cs            # IFryPlugin overlay registration & ALC resolvers
├── MusicPlayerViewModel.cs         # SoundFlow engine, playback state, and commands
├── TrackViewModel.cs               # ID3 tags, cover art, and per-track observable model
├── WavySlider.cs                   # Custom Avalonia tactile animated slider
├── RepeatModeIconConverter.cs      # Repeat mode enum to Material icon converter
├── MusicPlayerView.axaml / .cs     # M3 Expressive floating window interface
├── plugin.json                     # FryPDF marketplace manifest
├── Runner/                         # Self-contained standalone preview application
│   ├── MusicPlayerPlugin.Runner.csproj
│   ├── Program.cs / App.axaml
│   └── MainWindow.axaml
└── Tests/                          # xUnit unit test suite
    └── MusicPlayerPlugin.Tests.csproj
```

---

## 📜 License
MIT License. See [LICENSE](LICENSE) for details.
