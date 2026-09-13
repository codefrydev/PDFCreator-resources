# FryPDF Chess Game Plugin (CPU vs. User)

A complete, self-contained example plugin for **FryPDF** implementing a playable, draggable **Chess** game against an intelligent computer opponent (CPU vs. User) in a floating shell overlay or standalone runner.

---

## Features

- ♟️ **Floating Draggable Shell Overlay**: Mounted to `shell.overlay` with standard Material Design 3 chrome (draggable header, pin, minimize, close).
- 🤖 **Intelligent CPU Opponent**:
  - **Easy**: Casual mode with tactical captures and blunder variance.
  - **Medium**: Depth 3 minimax search with Alpha-Beta pruning and Piece-Square Tables (PST).
  - **Hard**: Depth 4 Alpha-Beta search with quiescence evaluation, move ordering (MVV-LVA), and checkmate avoidance.
  - **Zero UI Stutter**: All AI computations execute on a background thread via `Task.Run()` with `CancellationToken` support.
- 📜 **Full FIDE Chess Rules**:
  - Legal move generation with pin/check restrictions.
  - Special moves: **En passant**, **Kingside & Queenside castling**, and interactive **pawn promotion** (Queen, Rook, Bishop, Knight).
  - Check, checkmate, stalemate, and 50-move draw detection.
- 🎨 **Material Design 3 (M3) Expressive Styling**:
  - High-contrast board themes: **Emerald** (Modern Tournament), **Classic Wood**, and **Midnight Slate**.
  - Interactive visual cues: Legal move destination dots, enemy capture rings, last-move highlights, and king-in-check red alert.
  - Captured pieces row with real-time material advantage point tracker (`+3`, `+1`).
- ⚡ **Productivity & Shell Integration**:
  - **Command Palette**: Press `Ctrl+Alt+C` to toggle the Chess overlay.
  - **Status Bar**: Interactive footer pill (`♟️ Chess`) with one-click launcher.
  - **Ribbon Action**: Quick action in the Ribbon's **View** tab.
  - **Declarative Settings**: Auto-generated M3 settings schema for AI difficulty, board theme, legal move hints, and board coordinates.
- 🏃 **Standalone Runner Application**: Run and test the plugin independently via `ChessPlugin.slnx` or `ChessPlugin.Runner.csproj` without launching the host FryPDF application.

---

## Project Structure

```
ChessPlugin/
├── ChessPlugin.slnx                  # Modern XML solution (Plugin + Runner)
├── ChessPlugin.csproj                # .NET 10 project file with automated .fryplugin packager
├── plugin.json                       # Manifest with metadata, icon, and settings schema
├── ChessPlugin.cs                    # IFryPlugin implementation and capability registrations
├── Engine/                           # Pure C# zero-dependency chess engine
│   ├── Piece.cs                      # Piece types, colors, values, and unicode glyphs
│   ├── Square.cs                     # Square indexing and coordinate helpers (a1-h8)
│   ├── Move.cs                       # Move structs, flags, and undo state
│   ├── Board.cs                      # 8x8 Board state, check validation, and PST evaluation
│   ├── MoveGenerator.cs              # Pseudo-legal & legal move generator and SAN notation
│   └── ChessAi.cs                    # Multi-tier asynchronous CPU engine
├── ViewModels/                       # Reactive MVVM layer
│   ├── ChessViewModel.cs             # Game orchestrator, async CPU turns, scoreboard
│   ├── ChessSquareViewModel.cs       # Individual square visual state, theme, highlights
│   └── MoveHistoryItemViewModel.cs   # Move history items
├── Views/                            # Avalonia M3 Expressive views
│   ├── ChessView.axaml               # Interactive chessboard layout & overlays
│   └── ChessView.axaml.cs            # View code-behind
├── README.md                         # This file
└── Runner/                           # Standalone test host application
    ├── ChessPlugin.Runner.csproj     # WinExe runner project
    ├── Program.cs                    # Avalonia bootstrap with Inter font
    ├── App.axaml / App.axaml.cs      # Theme resources
    ├── MainWindow.axaml / .cs        # Test preview window
    └── StandaloneSettingsStore.cs    # Isolated JSON settings store
```

---

## Building and Packaging

Run the following command in this directory:

```bash
dotnet build -c Release
```

MSBuild will compile the plugin and automatically package it into:
```
bin/Release/net10.0/Chess.fryplugin
```

To package and stage into the marketplace repository:
```bash
python3 tools/package_plugin.py ChessPlugin
```

---

## Installing into FryPDF

### Option 1: Drag-and-Drop (Recommended)
1. Open **FryPDF**.
2. Open the **Plugins Manager** (Click Settings $\to$ **Plugins & Extensions**, or press `Ctrl+Shift+P` / `⌘Shift+P`).
3. Drag and drop `Chess.fryplugin` onto the dialog window.
4. The game mounts immediately without restarting!

### Option 2: Command Palette
1. In FryPDF, press `Ctrl+K` or `⌘K` to open the **Command Palette**.
2. Type `Install Plugin Package...` and select `Chess.fryplugin`.

### Option 3: Manual Folder Discovery
Copy `Chess.fryplugin` (or the folder containing `ChessPlugin.dll` and `plugin.json`) into:
- **macOS**: `~/Library/Application Support/FryPdf/plugins/`
- **Windows**: `%APPDATA%\FryPdf\plugins\`
- **Linux**: `~/.config/FryPdf/plugins/`
