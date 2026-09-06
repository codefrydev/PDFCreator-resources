# Agent Development Guidelines: FryPDF Resources & Plugin Hub

This repository (`codefrydev/PDFCreator-resources` / `PDFCreator-Fonts`) is the official **Resources, Typography CDN, and Remote Plugin Marketplace Ecosystem** for **FryPDF** (the modern .NET 10 Avalonia cross-platform document studio).

All AI agents, subagents, and human contributors working in this repository **must strictly adhere to the guidelines and mandates detailed below**.

---

## 1. Repository Anatomy

```
PDFCreator-resources/
├── AGENTS.md                                 # Master guidelines for AI agents & contributors
├── README.md                                 # Human-facing repository documentation
├── catalog.json -> plugins/catalog.json     # Root marketplace catalog symlink/reference
├── examples/                                 # Reference plugin source code & standalone runners
│   ├── SnakePlugin/                          # Retro Arcade Snake game overlay
│   │   ├── SnakePlugin.slnx                  # Modern XML solution (Plugin + Runner)
│   │   ├── SnakePlugin.csproj                # Dual-mode .NET 10 project + packaging target
│   │   ├── SnakeGamePlugin.cs                # IFryPlugin implementation
│   │   ├── SnakeGameView.axaml / .cs         # M3 Expressive Avalonia view
│   │   ├── SnakeGameViewModel.cs             # CommunityToolkit.Mvvm reactive ViewModel
│   │   ├── plugin.json                       # Plugin manifest and declarative settings
│   │   ├── README.md                         # Plugin architecture & guide
│   │   └── Runner/                           # Standalone test host application
│   │       ├── SnakePlugin.Runner.csproj     # WinExe runner project
│   │       ├── Program.cs / App.axaml        # Avalonia bootstrap with Inter font
│   │       ├── MainWindow.axaml / .cs        # Test preview window
│   │       └── StandaloneSettingsStore.cs    # Isolated JSON settings store
│   ├── TicTacToePlugin/                      # Tic-Tac-Toe mini-game with minimax AI
│   ├── ScratchpadPlugin/                     # Review scratchpad and markdown notes
│   ├── TelemetryPlugin/                      # Document telemetry and GC memory HUD
│   └── MusicPlayerPlugin/                    # Playlist music player overlay (LibVLC)
├── plugins/                                  # Marketplace distribution center
│   ├── catalog.json                          # Official FryPDF remote marketplace registry
│   ├── frypdf.overlay.snake/                 # Snake.fryplugin, plugin.json, README.md
│   ├── com.frypdf.plugin.tictactoe/          # TicTacToe.fryplugin, plugin.json, README.md
│   ├── frypdf.overlay.scratchpad/            # Scratchpad.fryplugin, plugin.json, README.md
│   ├── frypdf.overlay.telemetry/             # Telemetry.fryplugin, plugin.json, README.md
│   └── frypdf.overlay.musicplayer/           # MusicPlayer.fryplugin, plugin.json, README.md
├── fonts/                                    # 67+ Open-source TrueType & OpenType fonts
├── tools/                                    # Validation & packaging automation utilities
│   ├── validate_plugins.py                   # Integrity check for catalog, manifests, packages
│   ├── package_plugin.py                     # Release build & packaging automation CLI
│   └── verify_fonts.py                       # Font format & binary header validator
└── .agents/                                  # Repository AI customizations
    ├── rules/                                # Architecture, UI, performance, and plugin mandates
    │   ├── plugin_authoring_and_marketplace_mandate.md
    │   ├── plugin_pattern_mandate.md
    │   ├── m3_expressive_ui_mandate.md
    │   └── performance_and_zero_lag_mandate.md
    └── skills/                               # Reusable agent skills
        ├── avalonia/                         # Avalonia UI framework guidance
        └── frypdf-plugin-authoring/          # Step-by-step plugin creation & packaging
```

---

## 2. Core Mandates for Plugin Development

### Rule 1: Every Plugin MUST Have a Standalone `Runner/` Application
Developers and AI agents must be able to open `<PluginName>Plugin.slnx` in JetBrains Rider or Visual Studio, hit **Run (F5)**, and instantly test the plugin UI in a standalone preview window without launching the host FryPDF application:
- `Runner/<PluginName>Plugin.Runner.csproj`: Targets `net10.0`, `<OutputType>WinExe</OutputType>`, references the plugin project.
- `Runner/Program.cs`: Configures `AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()`.
- `Runner/App.axaml`: Loads `FluentTheme`, `Material.Icons.Avalonia`, and M3 Expressive resources.
- `Runner/MainWindow.axaml`: Hosts the plugin view with appropriate margin, title, and dark backdrop (`#090D13` or `{DynamicResource M3SurfaceBrush}`).
- `Runner/StandaloneSettingsStore.cs`: Implements minimal `IFryPluginContext` or standalone service provider backed by local JSON.

### Rule 2: Dual-Mode `.csproj` Configuration
The main plugin `.csproj` must support both in-tree sibling development (when the `PDFCreator` repo is cloned alongside) and standalone builds:
```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <LangVersion>13</LangVersion>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <DefaultItemExcludes>$(DefaultItemExcludes);Runner/**</DefaultItemExcludes>
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>
```
Isolate `Runner/**` so the runner sources are never compiled into the plugin assembly!

### Rule 3: Automated `.fryplugin` Packaging Target
Every plugin `.csproj` must include the `PackageFryPlugin` MSBuild target triggered on `Condition="'$(Configuration)' == 'Release'"` to automatically assemble the `<Name>.fryplugin` zip archive containing `plugin.json` and compiled DLLs.

### Rule 4: Manifest (`plugin.json`) and Catalog Integrity
- Every plugin must have a valid `plugin.json` at its project root.
- The `id` must follow reverse-DNS format (`frypdf.overlay.<name>` or `com.<publisher>.plugin.<name>`).
- When a plugin is published or updated:
  1. Build Release: `dotnet build -c Release` (or `python3 tools/package_plugin.py <name>`).
  2. The `.fryplugin`, `plugin.json`, and `README.md` must be staged into `plugins/<id>/`.
  3. An entry must exist in `plugins/catalog.json` with matching `id`, `version`, `downloadUrl`, `formattedSize`, `tags`, `highlights`, and `contributedFeatures`.
  4. Always verify with: `python3 tools/validate_plugins.py`.

### Rule 5: Material Design 3 (M3) Expressive Aesthetics
- Follow the guidelines in `.agents/rules/m3_expressive_ui_mandate.md`.
- Use M3 shape tokens: full pills (`M3ShapeCornerFull` / `9999`) for buttons, segmented capsules, and chips; `16px` (`M3ShapeCornerLarge`) for cards and inputs.
- Never hardcode light/dark hex colors that break theme switching. Bind to dynamic resource brushes (`{DynamicResource M3PrimaryBrush}`, `{DynamicResource M3SurfaceContainer...}`).
- Overlay widgets must feature custom draggable window chrome with drag handles, title, and pin/close actions.

### Rule 6: Performance, Zero Lag, and Clean Unmounting
- Follow `.agents/rules/performance_and_zero_lag_mandate.md`.
- Zero UI blocking: All heavy computations, I/O, or audio/video decoding must occur off the Avalonia UI thread.
- Reversible effects: Registrations must use `PluginScope` (`ctx.RegisterEffect(...)`) so unmounting cleans up completely.
- Timers (`DispatcherTimer`) and background tasks must be stopped on unmount.
- Use `WeakReferenceMessenger` for decoupled pub/sub messaging to prevent memory leaks.

---

## 3. Standard Verification Checklist

Before completing any task or committing changes:

```bash
# 1. Verify all plugin solutions build with 0 warnings & 0 errors
dotnet build examples/SnakePlugin/SnakePlugin.slnx
dotnet build examples/TicTacToePlugin/TicTacToePlugin.slnx
dotnet build examples/ScratchpadPlugin/ScratchpadPlugin.slnx
dotnet build examples/TelemetryPlugin/TelemetryPlugin.slnx
dotnet build examples/MusicPlayerPlugin/MusicPlayerPlugin.slnx

# 2. Package and stage release packages (if plugin code or manifests changed)
python3 tools/package_plugin.py --all

# 3. Run ecosystem validation
python3 tools/validate_plugins.py

# 4. Verify typography library
python3 tools/verify_fonts.py
```
