# Retro Arcade Snake Game Plugin (`frypdf.overlay.snake`)

An interactive, non-modal floating arcade game plugin for **FryPDF** demonstrating the Cordis-inspired `shell.overlay` slot architecture.

---

## Features
- **Non-Modal Floating Overlay**: Runs smoothly on top of any document without interrupting editing or reading.
- **60+ FPS Rendering**: Avalonia `DrawingContext` direct visual drawing with zero Large Object Heap allocations.
- **On-Screen D-Pad & Keyboard Controls**: Use arrow keys, <kbd>W</kbd><kbd>A</kbd><kbd>S</kbd><kbd>D</kbd>, or touch/click the on-screen tactile D-Pad.
- **Configurable Settings**: Game Speed (Normal, Fast, Zen) and Wall Collision toggle.
- **Deep Shell Integration**: Contributes to Command Palette (`Ctrl+Alt+S`), Status Bar (`🐍 Snake`), and Ribbon View Tab.

---

## Building the Plugin

To compile the plugin and generate the `.fryplugin` release archive:

```bash
dotnet build SnakePlugin.csproj -c Release
```

The output package `Snake.fryplugin` will be created in `bin/Release/net10.0/Snake.fryplugin`.

---

## Installing into FryPDF

1. Open **FryPDF**.
2. Go to **Plugins & Extensions Studio** (via left sidebar, View tab, or <kbd>Ctrl</kbd>+<kbd>K</kbd>).
3. Search for **"Snake"** in the **Store** tab and click **Install**.
   - Or click **"Install from File..."** in the top-right corner and select `Snake.fryplugin`.
