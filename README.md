# FryPDF Resources & Plugin Ecosystem Hub

Official open-source repository hosting the **Remote Plugin Marketplace**, **Reference Plugin SDK**, and **Typography Font CDN** for **[FryPDF](https://github.com/codefrydev/PDFCreator)** (the modern .NET 10 cross-platform Avalonia document studio).

[![Plugins Catalog](https://img.shields.io/badge/Marketplace-5%20Plugins-blue.svg)](plugins/catalog.json)
[![Fonts](https://img.shields.io/badge/Fonts-67%20Binaries-green.svg)](fonts/)
[![License](https://img.shields.io/badge/License-MIT%20%2F%20OFL-orange.svg)](LICENSE)

---

## 🌟 Ecosystem Highlights

| Domain | Description | Location |
|---|---|---|
| **Plugin Marketplace** | Remote distribution packages (`.fryplugin`), manifests, and central catalog | [`plugins/`](plugins/) |
| **Plugin SDK & Examples** | Full C# reference implementations with standalone Avalonia test runners | [`examples/`](examples/) |
| **Typography CDN** | 67 curated multilingual TrueType and OpenType fonts | [`fonts/`](fonts/) |
| **Automation Tooling** | Validation and packaging utilities | [`tools/`](tools/) |
| **Developer Guidelines** | Strict architectural, UI, and performance rules | [`AGENTS.md`](AGENTS.md) & [`.agents/rules/`](.agents/rules/) |

---

## 🧩 1. FryPDF Plugin Marketplace

FryPDF features a dynamic microkernel architecture where extensions mount directly into the application runtime (`shell.overlay`, ribbon tabs, status bar, command palette, and tools studio) without requiring full application restarts.

The central marketplace catalog is defined in [`plugins/catalog.json`](plugins/catalog.json) and served over GitHub Raw CDN.

### Featured Plugins

| Plugin | ID | Slot | Description | Package |
|---|---|---|---|---|
| 🎮 **Tic-Tac-Toe** | `com.frypdf.plugin.tictactoe` | `shell.overlay` | Playable 2-Player & Minimax AI game overlay | [TicTacToe.fryplugin](plugins/com.frypdf.plugin.tictactoe/TicTacToe.fryplugin) |
| 🐍 **Retro Arcade Snake** | `frypdf.overlay.snake` | `shell.overlay` | 60+ FPS direct canvas retro arcade game | [Snake.fryplugin](plugins/frypdf.overlay.snake/Snake.fryplugin) |
| 📝 **Review Scratchpad** | `frypdf.overlay.scratchpad` | `shell.overlay` | Floating Markdown notes with word counters | [Scratchpad.fryplugin](plugins/frypdf.overlay.scratchpad/Scratchpad.fryplugin) |
| ⚡ **Telemetry HUD** | `frypdf.overlay.telemetry` | `shell.overlay` | Real-time managed heap & GC memory monitor | [Telemetry.fryplugin](plugins/frypdf.overlay.telemetry/Telemetry.fryplugin) |
| 🎵 **Music Player** | `frypdf.overlay.musicplayer` | `shell.overlay` | Playlist audio player powered by LibVLC | [MusicPlayer.fryplugin](plugins/frypdf.overlay.musicplayer/MusicPlayer.fryplugin) |

---

## 🚀 2. Standalone Plugin Development (Zero Friction)

Every reference plugin in [`examples/`](examples/) includes a modern `.slnx` solution and a standalone `Runner/` application.

### Instant F5 Preview & Debugging
You do **not** need to build or run the heavy FryPDF host application to develop and debug plugins!
1. Open any plugin solution in **JetBrains Rider** or **Visual Studio** (e.g. `examples/SnakePlugin/SnakePlugin.slnx`).
2. Set the `Runner` project as your startup project.
3. Hit **Run (F5)**! A dark-themed test window opens immediately with live hot-reload, pixel-accurate Material Design 3 tokens, and mock settings.

```
examples/SnakePlugin/
├── SnakePlugin.slnx                  # Solution uniting Plugin + Runner
├── SnakePlugin.csproj                # Dual-mode .NET 10 project + packaging target
├── SnakeGamePlugin.cs                # IFryPlugin implementation
├── SnakeGameView.axaml / .cs         # M3 Expressive Avalonia view
├── SnakeGameViewModel.cs             # CommunityToolkit.Mvvm reactive ViewModel
├── plugin.json                       # Plugin manifest & declarative settings schema
└── Runner/                           # Standalone test application
    ├── SnakePlugin.Runner.csproj     # WinExe executable project
    ├── Program.cs / App.axaml        # Avalonia bootstrap with Inter font
    ├── MainWindow.axaml / .cs        # Test window preview
    └── StandaloneSettingsStore.cs    # Mock settings store
```

---

## 📦 3. Building, Packaging & Publishing

### Automated CLI Tooling

Package all plugins into their `.fryplugin` release archives and synchronize metadata:

```bash
# Package all plugins in examples/ and update catalog sizes
python3 tools/package_plugin.py --all

# Or package a single plugin
python3 tools/package_plugin.py SnakePlugin
```

### Validate Ecosystem Integrity

Before committing or pushing, run the comprehensive integrity validator:

```bash
python3 tools/validate_plugins.py
```
This automatically verifies:
- `plugins/catalog.json` syntax, schema, and unique IDs
- Parity between catalog entries and `plugins/<id>/` distribution folders
- `.fryplugin` zip archive contents (`plugin.json` and compiled `.dll` assemblies)
- Solution and standalone runner health across all plugins in `examples/`

---

## 🔤 4. Typography & Font Resources

Official typography and multilingual font library distributed via CDN for **FryPDF**.

- **Total Fonts**: 67 TrueType & OpenType font files
- **Total Library Size**: ~80.7 MB
- **Raw CDN Endpoint**:
  ```
  https://raw.githubusercontent.com/codefrydev/PDFCreator-resources/refs/heads/main/fonts/{fileName}
  ```

### Usage in FryPDF
Fonts are fetched on demand via `FontPackageService.cs`:
```csharp
public const string FontCdnBaseUrl = "https://raw.githubusercontent.com/codefrydev/PDFCreator-resources/refs/heads/main/fonts";
```
Downloaded fonts are cached locally in `AppData/FryPDF/FontPackages/` and dynamically registered into QuestPDF and Avalonia graphics pipelines without requiring application restarts.

### Font Verification
Verify all font binaries with:
```bash
python3 tools/verify_fonts.py
```

### Licensing Information
All fonts in this repository are distributed under permissive open-source licenses:
- **SIL Open Font License 1.1** (64 fonts): See [`OFL.txt`](./OFL.txt)
- **Apache License 2.0** (`Roboto.ttf`, `RobotoMono.ttf`): See [`LICENSE-Apache-2.0.txt`](./LICENSE-Apache-2.0.txt)
- **Ubuntu Font Licence 1.0** (`Ubuntu.ttf`): See [`LICENSE-Ubuntu.txt`](./LICENSE-Ubuntu.txt)

---

## 📂 Repository Directory Layout

```
PDFCreator-resources/
├── AGENTS.md                                 # Master guidelines for AI agents & contributors
├── README.md                                 # This file
├── LICENSE                                   # Full copyright & author attributions
├── OFL.txt                                   # SIL Open Font License 1.1 text
├── LICENSE-Apache-2.0.txt                    # Apache License 2.0 text
├── LICENSE-Ubuntu.txt                        # Ubuntu Font Licence text
├── examples/                                 # C# source code & standalone runners
│   ├── SnakePlugin/                          # Retro arcade snake game
│   ├── TicTacToePlugin/                      # Tic-tac-toe with minimax AI
│   ├── ScratchpadPlugin/                     # Review scratchpad and markdown notes
│   ├── TelemetryPlugin/                      # Managed heap & GC telemetry HUD
│   └── MusicPlayerPlugin/                    # Playlist audio player overlay
├── plugins/                                  # Marketplace distribution center
│   ├── catalog.json                          # Central marketplace catalog registry
│   ├── frypdf.overlay.snake/                 # Snake.fryplugin release package
│   ├── com.frypdf.plugin.tictactoe/          # TicTacToe.fryplugin release package
│   ├── frypdf.overlay.scratchpad/            # Scratchpad.fryplugin release package
│   ├── frypdf.overlay.telemetry/             # Telemetry.fryplugin release package
│   └── frypdf.overlay.musicplayer/           # MusicPlayer.fryplugin release package
├── fonts/                                    # 67 TrueType & OpenType font binaries
├── tools/                                    # Diagnostic & packaging utilities
│   ├── validate_plugins.py                   # Catalog & ecosystem validator
│   ├── package_plugin.py                     # Automated packager for examples/
│   └── verify_fonts.py                       # Typography integrity validator
└── .agents/                                  # AI agent customizations
    ├── rules/                                # Strict development mandates
    │   ├── plugin_authoring_and_marketplace_mandate.md
    │   ├── plugin_pattern_mandate.md
    │   ├── m3_expressive_ui_mandate.md
    │   └── performance_and_zero_lag_mandate.md
    └── skills/                               # Reusable agent skills
        ├── avalonia/                         # Avalonia UI framework guidance
        └── frypdf-plugin-authoring/          # Step-by-step plugin authoring guide
```
