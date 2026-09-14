# FryPDF Tic-Tac-Toe Plugin Example

A complete, self-contained external plugin example for **FryPDF** implementing a playable, draggable **Tic-Tac-Toe** mini-game floating overlay.

---

## Features

- 🎮 **Floating Draggable Shell Overlay**: Mounted to `shell.overlay` with standard Material Design 3 chrome (pin, minimize, close, draggable header).
- 🤖 **Two Game Modes**: Single-Player vs Smart AI or pass-and-play Local 2-Player mode.
- 🏆 **Live Score Tracking**: Persistent counters for Player X wins, Player O wins, and Draws.
- 🎨 **Material Design 3 (M3) Expressive Styling**: Adheres to FryPDF's token system (`M3ShapeCornerFull`, `M3ShapeCornerLarge`, dynamic tonal brushes).
- ⌨️ **Command Palette Integration**: Trigger instantly via `Ctrl+Alt+T` / `⌘Alt+T`.
- 🕹️ **Footer Status Bar Widget**: Quick-toggle pill button (`🎮 Tic-Tac-Toe`) in the footer status bar.
- 🎗️ **Ribbon Action**: Quick launcher in the Ribbon's **View** tab.
- ⚙️ **Declarative Settings**: Auto-generated M3 settings schema for initial mode, AI difficulty, and sound effects.

---

## Project Structure

```
TicTacToePlugin/
├── TicTacToePlugin.csproj    # .NET 10 project file with automated .fryplugin packager
├── plugin.json               # Manifest with metadata, icon, and settings schema
├── TicTacToePlugin.cs        # IFryPlugin implementation and capability registrations
├── TicTacToeViewModel.cs     # Reactive MVVM game state, win detection & AI logic
├── TicTacToeView.axaml       # Avalonia XAML Material Design 3 Expressive view
├── TicTacToeView.axaml.cs    # Code-behind
└── README.md                 # This file
```

---

## Building and Packaging

Run the following command in this directory:

```bash
dotnet build -c Release
```

MSBuild will compile the plugin and automatically package it into:
```
bin/Release/net10.0/TicTacToe.fryplugin
```

---

## Installing into FryPDF

### Option 1: Drag-and-Drop (Recommended)
1. Open **FryPDF**.
2. Open the **Plugins Manager** (Click the Settings gear in the header $\to$ **Plugins & Extensions**, or press `Ctrl+Shift+P` / `⌘Shift+P`).
3. Drag and drop `TicTacToe.fryplugin` onto the dialog window.
4. The game mounts immediately without restarting!

### Option 2: Command Palette
1. In FryPDF, press `Ctrl+K` or `⌘K` to open the **Command Palette**.
2. Type `Install Plugin Package...` and select `TicTacToe.fryplugin`.

### Option 3: Manual Auto-Discovery Folder
Copy `TicTacToe.fryplugin` (or the folder containing `TicTacToePlugin.dll` and `plugin.json`) into your user plugins directory:
- **macOS**: `~/Library/Application Support/FryPdf/plugins/`
- **Windows**: `%APPDATA%\FryPdf\plugins\`
- **Linux**: `~/.config/FryPdf/plugins/`
