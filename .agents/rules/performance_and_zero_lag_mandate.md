# STRICT PERFORMANCE MANDATE: Zero-Lag, 60+ FPS & Responsive Architecture

**APPLIES TO ALL AI AGENTS AND DEVELOPERS**:
FryPDF is an interactive, real-time desktop document studio. **UI lag, frame drops, blocking operations, stuttering transitions, and high memory retention are strictly forbidden.**
Whenever you write or modify code in FryPDF, you **MUST ALWAYS THINK IN TERMS OF PERFORMANCE OPTIMIZATION**.

---

## 1. Zero UI Thread Blocking
- **Never Run Heavy Operations on the UI Thread**:
  - PDF deconstruction, parsing, Skia rendering, QuestPDF generation, OCR execution, AI inference, and disk/network I/O **must always be offloaded** via `Task.Run` or asynchronous commands.
- **Never Block Asynchronously**:
  - Calling `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on the Avalonia UI thread is strictly forbidden.
- **Always Pass `CancellationToken`**:
  - Accept and propagate cancellation tokens throughout long-running operations so users can instantly cancel actions or switch pages without background worker orphan threads.

---

## 2. Instant Navigation & View Caching (0ms Latency)
- **Cache Contributed Plugin Views**:
  - In navigation controllers (such as `HomeViewModel`), always cache dynamically created views in an instance dictionary (`_dynamicViewCache`). Never destroy and re-instantiate heavy visual trees when navigating between tabs.
- **Pre-Mounted Static Views**:
  - Built-in primary pages (Overview Dashboard, Tools Studio, PDF Reader Landing) should have pre-mounted XAML elements in `HomeView.axaml` to guarantee 0ms instantaneous switching without layout compilation overhead.

---

## 3. Virtualization & Recycling
- **Always Virtualize Dynamic Collections**:
  - Always use `ItemsRepeater` or `VirtualizingStackPanel` with `ScrollUnit="Pixel"` for lists with variable or large items (document thumbnails, tool card grids, audit logs, tabular data).
  - Never use an unvirtualized `StackPanel` inside a `ScrollViewer` for collections larger than 10 items.

---

## 4. Continuous Input Throttling & Debouncing
- **Pinch-to-Zoom Math**:
  - Calculate trackpad pinch gestures multiplicatively and clamp strictly:
    `Math.Clamp(Math.Round(currentZoom * (1.0 + delta), 3), 0.1, 5.0)` to eliminate exponential explosion.
- **Debounced Filters**:
  - Debounce search queries and text filters (150–250ms) to avoid executing filter passes on every keystroke.
- **Throttled Sliders**:
  - Do not trigger expensive Skia repaints or document recompilations on every slider micro-tick; throttle updates during drag.

---

## 5. Memory Management & Unmanaged Resource Disposal
- **Large Object Heap (LOH) Avoidance**:
  - Any byte array $\ge 85\text{ KB}$ is allocated on the LOH and causes GC fragmentation. Use `ArrayPool<byte>.Shared` and `MemoryStream` buffers.
  - Never store Base64 strings in memory for image or vector assets; store raw `byte[] ImageData` directly and compute Base64 only lazily for JSON export.
- **Dispose Unmanaged Skia Objects Immediately**:
  - `SKBitmap`, `SKImage`, `SKSurface`, `SKData`, and Avalonia `WriteableBitmap` hold native unmanaged pointers.
  - Always wrap temporary Skia objects in `using` blocks.
  - Always dispose old bitmaps before assigning replacement preview bitmaps.
- **Weak Subscriptions**:
  - Use `WeakReferenceMessenger` for pub/sub messaging across ViewModels to avoid memory retention cycles that prevent garbage collection.

---

## 6. Rendering & Frame Budgets
- **Frame Budgets**:
  - Maintain 16ms per frame (60 FPS) and 8ms per frame (120 FPS).
- **Favor `RenderTransform` Over Layout Mutations**:
  - Use GPU-composited `RenderTransform` rather than mutating layout properties (`Margin`, `Width`, `Height`) during animations or gesture interactions to avoid triggering costly synchronous layout reflows.
- **Keep Visual Trees Shallow**:
  - Avoid deeply nested `Border` and `Grid` hierarchies.

---

## 7. Verification
- Verify that `dotnet build` compiles cleanly with zero warnings (`TreatWarningsAsErrors=true`).
- Verify unmount and teardown logic: Confirm that all timers (`DispatcherTimer`), observables, and event subscriptions unregister via `PluginScope` (`ctx.RegisterEffect(...)`) with zero memory leaks.
- Run ecosystem validation:
  ```bash
  python3 tools/validate_plugins.py
  ```

