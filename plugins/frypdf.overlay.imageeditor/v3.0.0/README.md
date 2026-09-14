# 🎨 Image Editor Plugin

A Canva-style layered design editor overlay for **FryPDF** — compose graphics directly inside your PDF workspace without leaving the application.

## Features

- **Layer-based canvas** — stack Text, Rectangle, Ellipse, Arrow, and imported Images in Z-order, with a clickable **Layers panel** (select/visibility/lock)
- **Click-to-place tools & Eyedropper** — click or drag directly on the canvas to place shapes/text; `V`/`T`/`R`/`E`/`A` keyboard shortcuts switch tools; sample any on-canvas color with the eyedropper
- **Multi-select & Grouping** — Shift+Click or marquee-drag to select multiple elements; group/ungroup, group move, group resize, group rotate, Z-order, align (left/right/top/bottom/center) and distribute
- **Full style controls** — fill & stroke color pickers, gradient fills (linear/radial, angle + 2-stop editor), stroke width/dash style (solid/dashed/dotted), corner radius, opacity, and precise numeric X/Y/Width/Height/Rotation fields
- **Free-angle rotation** — drag-to-rotate any single element or a whole multi-selection around a shared pivot, with 15° Shift-snap
- **Smart alignment guides** — snapping guide lines against other elements' edges/centers and the canvas edges/center while dragging
- **Inline text editing** — double-click any text element to edit it directly on the canvas
- **Duplicate, Copy & Paste** — `Ctrl+D` to duplicate the selection; `Ctrl+C`/`Ctrl+V` for an in-app element clipboard
- **Undo/Redo** — full edit history (`Ctrl+Z` / `Ctrl+Y` / `Ctrl+Shift+Z`) across every mutation: moves, resizes, rotations, group operations, crop, and color adjustments
- **Zoom & Pan** — cursor-anchored zoom (`Ctrl`+wheel), pan (wheel, Shift+wheel, space-drag, middle-mouse-drag), and fit-to-window
- **Resizable window** — drag-resize the overlay itself (680×520 to 1400×1100), courtesy of the host's StandardCard chrome
- **Per-image crop** — drag-handle crop UI with confirm/cancel/reset, fixed-aspect-ratio presets (1:1, 4:3, 3:2, 16:9, 9:16, Original), aspect-ratio-locked resize (Shift inverts for one drag)
- **Rotate & Flip** — 90° rotate CW/CCW and horizontal/vertical flip, applied uniformly to every element type
- **Deep color adjustments & 9 filters** — brightness/contrast/saturation/hue/temperature/tint/blur/sharpen sliders, one-click Auto-Enhance, plus Black & White, Sepia, Vintage, Vivid, Noir, Invert, Duotone, and Vignette presets (SkiaSharp color-matrix + spatial filters, debounced off the UI thread)
- **Template Gallery** — 8 starter templates (Blank Slide, Certificate, Flyer, Photo Collage, Quote Card, Cover Page, Social Post, Sticky Note), each with a live-rendered preview
- **Project Save/Load** — save and reopen full editable projects as `.fryimg` files, with a confirm-before-discard safeguard on Clear/Load
- **Image import** — pick any PNG, JPG, BMP, GIF, or WebP from disk; scales to fit preserving aspect ratio, decoded off the UI thread
- **PNG export & real clipboard copy** — render the full canvas to a PNG file, or copy it as an actual image to the system clipboard
- **Background options** — White, Dark, or Transparent (checkerboard) canvas backgrounds
- **Material 3 Expressive UI** — fully theme-adaptive (light/dark), segmented tool/background selectors, tactile sliders
- **Shell Integration** — Command Palette (`Ctrl+Alt+I`), Status Bar pill, Ribbon button, with clean teardown (no leaked registrations) on unmount

## Shell Contributions

| Feature | Details |
|---|---|
| Workspace Studio Page | Left sidebar navigation (under Library group), order 170, blue `Studio` badge, full-viewport edge-to-edge design canvas |
| Shell Overlay | Resizable `Image Editor` card, 900 × 640 default (680×520 min – 1400×1100 max) |
| Command Palette | `Open Image Editor (Shell Overlay)` — `Ctrl+Alt+I` |
| Status Bar | `🎨 Image Editor` clickable pill |
| Ribbon | View Tab → Plugins → Image Editor |

## Settings

| Key | Type | Default | Description |
|---|---|---|---|
| `DefaultBackground` | select | `White` | Canvas background on open |
| `DefaultCanvasWidth` | integer | `640` | Canvas width in pixels |
| `DefaultCanvasHeight` | integer | `480` | Canvas height in pixels |

## Architecture notes

- `EditorCanvasControl.cs` owns rendering (zoom/pan viewport transform, per-element rotate/flip, multi-select outlines, rotation/group-resize handles, alignment guides, crop overlay) and all pointer/keyboard interaction. A tile-bitmap cache backs the dot-grid/checkerboard background so they're regenerated only when the canvas size changes, not on every render.
- `Models/Undo/` holds the undo/redo command infrastructure (`EditHistory`, per-mutation `IUndoableCommand` types, and a generic `GenericPropertyCommand<TTarget,TValue>`/`BatchPropertyCommand` for simple property swaps).
- `Models/Serialization/` is a separate DTO layer (`CanvasElementMapper`, `[JsonPolymorphic]` DTOs) for `.fryimg` project files — kept independent of the runtime model to avoid attribute-collision risk.
- `Models/Templates/` defines the 8 starter templates as element factories (no embedded asset files); gallery thumbnails are rendered live via `EditorCanvasControl.RenderElementsStatic`.
- `Models/ImageAdjustmentProcessor.cs` builds the brightness/contrast/saturation/hue/temperature/tint color matrices and blur/sharpen spatial filters applied via SkiaSharp; adjustments always recompute from the originally-imported bytes so they never compound.
- `ImageEditorViewModel.cs` is the single source of truth for undo history, selection, and all commands — no separate ViewModel is introduced for any sub-panel.

## Requirements

- FryPDF ≥ 1.0.0
- .NET 10.0 runtime

## License

MIT © FryPDF Team
