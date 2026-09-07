using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PdfEditorApp.Core.Plugins.Settings;
using PdfEditorApp.Plugins.ImageEditor.Models;
using PdfEditorApp.Plugins.ImageEditor.Models.Templates;
using PdfEditorApp.Plugins.ImageEditor.Models.Serialization;
using PdfEditorApp.Plugins.ImageEditor.Models.Undo;
using SkiaSharp;

namespace PdfEditorApp.Plugins.ImageEditor;

/// <summary>
/// MVVM ViewModel for the Image Editor plugin.
/// Manages the element collection, tool mode, color state, and all editor commands.
/// </summary>
public partial class ImageEditorViewModel : ObservableObject, IDisposable
{
    private const string PluginId = "frypdf.overlay.imageeditor";
    private readonly IPluginSettingsStore? _settingsStore;

    // ── Canvas state ───────────────────────────────────────────────────────
    [ObservableProperty]
    private ObservableCollection<CanvasElement> _elements = [];

    /// <summary>Layers list source, topmost element first (matches every other design tool's
    /// layer-list convention) — the raw Elements collection has no inherent Z order.</summary>
    public IEnumerable<CanvasElement> LayersDescendingZ => Elements.OrderByDescending(e => e.ZIndex);

    [RelayCommand]
    private void SelectElementFromLayerPanel(CanvasElement element) => SelectedElement = element;

    [ObservableProperty]
    private string _activeInspectorTab = "properties";

    [RelayCommand]
    private void SetInspectorTab(string tab) => ActiveInspectorTab = tab;

    public bool IsTextSelected => SelectedTextElement != null;
    public bool IsShapeSelected => SelectedFillable != null && SelectedImageElement == null && SelectedTextElement == null;
    public bool IsImageSelected => SelectedImageElement != null;
    public bool HasSelection => SelectedElement != null || HasMultiSelection;

    [RelayCommand]
    private void ToggleLayerVisibility(CanvasElement element)
    {
        element.IsVisible = !element.IsVisible;
        RefreshCanvas();
        OnPropertyChanged(nameof(LayersDescendingZ));
    }

    [RelayCommand]
    private void ToggleLayerLock(CanvasElement element)
    {
        element.IsLocked = !element.IsLocked;
        RefreshCanvas();
        OnPropertyChanged(nameof(LayersDescendingZ));
    }

    [RelayCommand]
    private void DeleteElementFromLayer(CanvasElement element)
    {
        var command = _isApplyingHistory ? null : new RemoveElementsCommand(Elements, [element]);
        Elements.Remove(element);
        if (SelectedElement == element) SelectedElement = null;
        SelectedElements.Remove(element);
        CanvasControl?.SetSelected(null);
        RefreshCanvas();
        if (command != null) History.Push(command);
        OnPropertyChanged(nameof(LayersDescendingZ));
    }

    [RelayCommand]
    private void MoveLayerUp(CanvasElement element)
    {
        SelectedElement = element;
        SelectedElements.Clear();
        SelectedElements.Add(element);
        BringForward();
    }

    [RelayCommand]
    private void MoveLayerDown(CanvasElement element)
    {
        SelectedElement = element;
        SelectedElements.Clear();
        SelectedElements.Add(element);
        SendBackward();
    }

    [RelayCommand]
    public void ResizeCanvas(string preset)
    {
        var parts = preset.Split('x');
        if (parts.Length == 2 && double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h))
        {
            _canvasWidth = w;
            _canvasHeight = h;
            OnPropertyChanged(nameof(CanvasWidth));
            OnPropertyChanged(nameof(CanvasHeight));
            CanvasSizeLabel = $"{(int)_canvasWidth} × {(int)_canvasHeight}";
            CanvasControl?.SetLogicalCanvasSize(_canvasWidth, _canvasHeight);
            CanvasControl?.FitToWindow();
            StatusMessage = $"Resized canvas to {CanvasSizeLabel}.";
        }
    }

    [ObservableProperty]
    private CanvasElement? _selectedElement;

    /// <summary>Full multi-selection mirrored from EditorCanvasControl. SelectedElement above
    /// stays the single-target convenience (null when 0 or 2+ are selected).</summary>
    [ObservableProperty]
    private ObservableCollection<CanvasElement> _selectedElements = [];

    public bool HasMultiSelection => SelectedElements.Count >= 2;
    public bool HasTripleSelection => SelectedElements.Count >= 3;

    /// <summary>Statically-typed convenience for XAML bindings that need ImageElement-only
    /// members (e.g. AspectLocked) — SelectedElement itself is CanvasElement-typed.</summary>
    public ImageElement? SelectedImageElement => SelectedElement as ImageElement;

    /// <summary>Statically-typed convenience properties so the Properties panel can bind
    /// generically (fill/stroke controls work for any shape implementing the interface,
    /// without a converter per concrete element type).</summary>
    public IHasFill? SelectedFillable => SelectedElement as IHasFill;
    public IHasStroke? SelectedStrokeable => SelectedElement as IHasStroke;
    public TextElement? SelectedTextElement => SelectedElement as TextElement;
    public RectangleElement? SelectedRectangleElement => SelectedElement as RectangleElement;

    public bool CanUngroupSelection => SelectedElements.Any(e => e.GroupId != null);

    [RelayCommand]
    private void GroupSelection()
    {
        if (SelectedElements.Count < 2) return;
        var before = SelectedElements.ToDictionary(e => e, e => e.GroupId);
        var groupId = Guid.NewGuid();
        foreach (var el in SelectedElements) el.GroupId = groupId;
        var after = SelectedElements.ToDictionary(e => e, e => e.GroupId);

        if (!_isApplyingHistory) History.Push(new GroupingChangeCommand(before, after));
        OnPropertyChanged(nameof(CanUngroupSelection));
        RefreshCanvas();
    }

    [RelayCommand]
    private void UngroupSelection()
    {
        var grouped = SelectedElements.Where(e => e.GroupId != null).ToList();
        if (grouped.Count == 0) return;
        var before = grouped.ToDictionary(e => e, e => e.GroupId);
        foreach (var el in grouped) el.GroupId = null;
        var after = grouped.ToDictionary(e => e, e => e.GroupId);

        if (!_isApplyingHistory) History.Push(new GroupingChangeCommand(before, after));
        OnPropertyChanged(nameof(CanUngroupSelection));
        RefreshCanvas();
    }

    /// <summary>Suppresses the SelectedElement→canvas echo while applying a selection that
    /// the canvas itself just reported, to avoid stomping a live multi-selection.</summary>
    private bool _isSyncingSelectionFromCanvas;

    /// <summary>Called by the view whenever EditorCanvasControl reports a selection change
    /// (click, shift-click, or marquee).</summary>
    public void UpdateSelectionFromCanvas(IReadOnlyList<CanvasElement> selection)
    {
        _isSyncingSelectionFromCanvas = true;
        try
        {
            SelectedElements.Clear();
            foreach (var el in selection) SelectedElements.Add(el);
            SelectedElement = selection.Count == 1 ? selection[0] : null;
        }
        finally { _isSyncingSelectionFromCanvas = false; }
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(HasTripleSelection));
        OnPropertyChanged(nameof(SelectedImageElement));
        OnPropertyChanged(nameof(CanUngroupSelection));
        OnPropertyChanged(nameof(SelectedFillable));
        OnPropertyChanged(nameof(SelectedStrokeable));
        OnPropertyChanged(nameof(SelectedTextElement));
        OnPropertyChanged(nameof(SelectedRectangleElement));
        OnPropertyChanged(nameof(IsTextSelected));
        OnPropertyChanged(nameof(IsShapeSelected));
        OnPropertyChanged(nameof(IsImageSelected));
        OnPropertyChanged(nameof(HasSelection));
        SyncAdjustmentSlidersToSelection();
        SyncStyleFieldsToSelection();
    }

    [ObservableProperty]
    private string _canvasSizeLabel = "640 × 480";

    /// <summary>Transient feedback surfaced in the view (errors, confirmations) — replaces
    /// the previous silent exception-swallowing in the async commands below.</summary>
    [ObservableProperty]
    private string _statusMessage = "Ready";

    private double _canvasWidth = 640;
    private double _canvasHeight = 480;

    /// <summary>The fixed logical canvas size — loaded once from settings at construction.</summary>
    public double CanvasWidth => _canvasWidth;
    public double CanvasHeight => _canvasHeight;

    // ── Tool state ─────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _activeToolMode = "select"; // select | text | rect | ellipse | arrow | image

    // ── Color state ────────────────────────────────────────────────────────
    [ObservableProperty]
    private Color _primaryColor = Color.FromArgb(255, 99, 102, 241);  // indigo

    [ObservableProperty]
    private Color _backgroundColor = Colors.White;

    [ObservableProperty]
    private string _backgroundColorName = "White";

    // ── Text tool state ────────────────────────────────────────────────────
    [ObservableProperty]
    private double _fontSize = 24;

    [ObservableProperty]
    private bool _isBold;

    [ObservableProperty]
    private bool _isItalic;

    [ObservableProperty]
    private string _newTextContent = "Text";

    // ── Canvas control reference (set by code-behind) ──────────────────────
    public EditorCanvasControl? CanvasControl { get; set; }

    // ── Undo / Redo ────────────────────────────────────────────────────────
    // One instance per overlay — every mutation (shapes, crop, rotate, adjustments)
    // pushes into this same history, never a separately-constructed one.
    public IEditHistory History { get; } = new EditHistory();
    private bool _isApplyingHistory;

    private bool CanUndo() => History.CanUndo;
    private bool CanRedo() => History.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        // A pending debounced style/adjustment command must never fire AFTER this — it would
        // push onto history with a stale "before" snapshot and wipe the redo stack this Undo
        // just built.
        CancelPendingAdjustmentDebounce();
        CancelPendingStyleDebounce();
        _isApplyingHistory = true;
        try
        {
            History.Undo();
            RefreshCanvas();
            OnPropertyChanged(nameof(LayersDescendingZ));
            OnPropertyChanged(nameof(CanUngroupSelection));
            SyncStyleFieldsToSelection();
            SyncAdjustmentSlidersToSelection();
        }
        finally { _isApplyingHistory = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        CancelPendingAdjustmentDebounce();
        CancelPendingStyleDebounce();
        _isApplyingHistory = true;
        try
        {
            History.Redo();
            RefreshCanvas();
            OnPropertyChanged(nameof(LayersDescendingZ));
            OnPropertyChanged(nameof(CanUngroupSelection));
            SyncStyleFieldsToSelection();
            SyncAdjustmentSlidersToSelection();
        }
        finally { _isApplyingHistory = false; }
    }

    /// <summary>Called by EditorCanvasControl once per completed drag (on pointer-release,
    /// never per-tick) so moves are a single undo step instead of hundreds.</summary>
    public void RecordElementsMoved(IReadOnlyList<(CanvasElement Element, Point OldPos, Point NewPos)> moves)
    {
        if (moves.Count == 0) return;
        if (!_isApplyingHistory) History.Push(new MoveElementsCommand(moves));
        SyncStyleFieldsToSelection();
    }

    /// <summary>Called by EditorCanvasControl once per completed resize (on pointer-release).</summary>
    public void RecordElementResized(CanvasElement element, Rect oldBounds, Rect newBounds)
    {
        if (!_isApplyingHistory) History.Push(new ResizeElementCommand(element, oldBounds, newBounds));
        SyncStyleFieldsToSelection();
    }

    /// <summary>Called by EditorCanvasControl once per completed proportional group-resize.</summary>
    public void RecordGroupResized(IReadOnlyList<(CanvasElement Element, Rect Old, Rect New)> changes)
    {
        if (changes.Count == 0) return;
        if (!_isApplyingHistory) History.Push(new GroupResizeCommand(changes));
        SyncStyleFieldsToSelection();
    }

    /// <summary>Called by EditorCanvasControl once per completed single-element rotation-handle drag.</summary>
    public void RecordElementRotated(CanvasElement element, (double X, double Y, double Rotation) before, (double X, double Y, double Rotation) after)
    {
        if (before == after) return;
        if (!_isApplyingHistory)
        {
            History.Push(new GenericPropertyCommand<CanvasElement, (double X, double Y, double Rotation)>(
                element, (t, v) => { t.X = v.X; t.Y = v.Y; t.RotationDegrees = v.Rotation; }, before, after, "Rotate element"));
        }
        SyncStyleFieldsToSelection();
    }

    /// <summary>Called by EditorCanvasControl once per completed multi-element rotation-handle drag.</summary>
    public void RecordGroupRotated(
        IReadOnlyDictionary<CanvasElement, (double X, double Y, double Rotation)> before,
        IReadOnlyDictionary<CanvasElement, (double X, double Y, double Rotation)> after)
    {
        if (!_isApplyingHistory) History.Push(new GroupRotateCommand(before, after));
        SyncStyleFieldsToSelection();
    }

    /// <summary>Called by the view's inline double-click text editor when the user commits an edit.</summary>
    public void CommitTextEdit(TextElement element, string newText)
    {
        if (element.Text == newText) return;
        var before = element.Text;
        element.Text = newText;
        if (!_isApplyingHistory)
        {
            History.Push(new GenericPropertyCommand<TextElement, string>(
                element, (t, v) => t.Text = v, before, newText, "Edit text"));
        }
        RefreshCanvas();
    }

    [RelayCommand]
    private void SetCropAspectPreset(string? ratioLabel)
    {
        double? ratio = ratioLabel switch
        {
            "1:1" => 1.0,
            "4:3" => 4.0 / 3.0,
            "3:2" => 3.0 / 2.0,
            "16:9" => 16.0 / 9.0,
            "9:16" => 9.0 / 16.0,
            _ => (double?)null, // "Original" — the source image's natural aspect ratio
        };
        CanvasControl?.SetCropAspectPreset(ratio);
    }

    // ── Crop (per-image) ───────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isCropping;

    [RelayCommand]
    private void EnterCropMode()
    {
        if (SelectedElement is not ImageElement) return;
        IsCropping = true;
        CanvasControl?.SetSelected(SelectedElement); // ensure canvas has exactly this single selection
        if (CanvasControl != null) CanvasControl.IsCropModeActive = true;
    }

    [RelayCommand]
    private void ConfirmCrop() => CanvasControl?.ConfirmCrop();

    [RelayCommand]
    private void CancelCrop() => CanvasControl?.CancelCrop();

    [RelayCommand]
    private void ResetCrop() => CanvasControl?.ResetCrop();

    /// <summary>Called by EditorCanvasControl after a crop is confirmed/reset/cancelled so the
    /// ViewModel's IsCropping mirrors the canvas and the change becomes undoable.</summary>
    public void RecordElementCropped(ImageElement element, ImageCropState before, ImageCropState after)
    {
        IsCropping = false;
        RefreshCanvas();
        if (_isApplyingHistory || before == after) return;
        History.Push(new GenericPropertyCommand<ImageElement, ImageCropState>(
            element,
            (e, s) => { e.SourceCropRect = s.SourceCropRect; e.X = s.X; e.Y = s.Y; e.Width = s.Width; e.Height = s.Height; },
            before, after, "Crop image"));
    }

    public void OnCropModeExited() => IsCropping = false;

    // ── Brightness / Contrast / Saturation / Hue / Temperature / Tint / Blur / Sharpen
    // / Filter presets (ImageElement only) ──────────────────────────────────
    [ObservableProperty]
    private double _imgBrightness;

    [ObservableProperty]
    private double _imgContrast;

    [ObservableProperty]
    private double _imgSaturation;

    [ObservableProperty]
    private double _imgHue;

    [ObservableProperty]
    private double _imgTemperature;

    [ObservableProperty]
    private double _imgTint;

    [ObservableProperty]
    private double _imgBlurRadius;

    [ObservableProperty]
    private double _imgSharpenAmount;

    [ObservableProperty]
    private string? _activeFilterPreset;

    private readonly DispatcherTimer _adjustmentDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private ImageElement? _pendingAdjustmentTarget;
    private ImageAdjustmentSnapshot? _adjustmentUndoSnapshot;

    private bool _isSyncingAdjustmentSliders;

    partial void OnImgBrightnessChanged(double value) { if (!_isSyncingAdjustmentSliders) QueueAdjustmentRecompute(el => el.Brightness = value); }
    partial void OnImgContrastChanged(double value) { if (!_isSyncingAdjustmentSliders) QueueAdjustmentRecompute(el => el.Contrast = value); }
    partial void OnImgSaturationChanged(double value) { if (!_isSyncingAdjustmentSliders) QueueAdjustmentRecompute(el => el.Saturation = value); }
    partial void OnImgHueChanged(double value) { if (!_isSyncingAdjustmentSliders) QueueAdjustmentRecompute(el => el.Hue = value); }
    partial void OnImgTemperatureChanged(double value) { if (!_isSyncingAdjustmentSliders) QueueAdjustmentRecompute(el => el.Temperature = value); }
    partial void OnImgTintChanged(double value) { if (!_isSyncingAdjustmentSliders) QueueAdjustmentRecompute(el => el.Tint = value); }
    partial void OnImgBlurRadiusChanged(double value) { if (!_isSyncingAdjustmentSliders) QueueAdjustmentRecompute(el => el.BlurRadius = value); }
    partial void OnImgSharpenAmountChanged(double value) { if (!_isSyncingAdjustmentSliders) QueueAdjustmentRecompute(el => el.SharpenAmount = value); }

    /// <summary>Refreshes the slider/preset display to match the newly-selected image,
    /// without treating the refresh itself as a user edit.</summary>
    private void SyncAdjustmentSlidersToSelection()
    {
        _isSyncingAdjustmentSliders = true;
        try
        {
            if (SelectedElement is ImageElement img)
            {
                ImgBrightness = img.Brightness;
                ImgContrast = img.Contrast;
                ImgSaturation = img.Saturation;
                ImgHue = img.Hue;
                ImgTemperature = img.Temperature;
                ImgTint = img.Tint;
                ImgBlurRadius = img.BlurRadius;
                ImgSharpenAmount = img.SharpenAmount;
                ActiveFilterPreset = img.ActiveFilterPreset;
            }
            else
            {
                ImgBrightness = 0; ImgContrast = 0; ImgSaturation = 0;
                ImgHue = 0; ImgTemperature = 0; ImgTint = 0;
                ImgBlurRadius = 0; ImgSharpenAmount = 0;
                ActiveFilterPreset = null;
            }
        }
        finally { _isSyncingAdjustmentSliders = false; }
    }

    [RelayCommand]
    private void SetFilterPreset(string? preset)
    {
        if (SelectedElement is not ImageElement img) return;
        var newPreset = string.Equals(img.ActiveFilterPreset, preset, StringComparison.Ordinal) ? null : preset;
        QueueAdjustmentRecompute(el => el.ActiveFilterPreset = newPreset);
        ActiveFilterPreset = newPreset;
    }

    /// <summary>Off-thread histogram analysis of the original image, setting
    /// Brightness/Contrast/Saturation to bring the image toward a balanced exposure.</summary>
    [RelayCommand]
    private async Task AutoEnhance()
    {
        if (SelectedElement is not ImageElement img || img.OriginalImageBytes is not { Length: > 0 } bytes) return;

        // A pending slider-drag debounce must not fire after this and clobber the enhancement
        // (or vice versa) with a stale "before" snapshot.
        CancelPendingAdjustmentDebounce();
        var before = SnapshotAdjustments(img);
        int token = ++img.AdjustmentVersion;

        var enhancement = await Task.Run(() =>
        {
            using var src = SKBitmap.Decode(bytes);
            if (src is null) return default;

            long sum = 0, sumSq = 0;
            long count = (long)src.Width * src.Height;
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    var px = src.GetPixel(x, y);
                    int luma = (int)(0.21 * px.Red + 0.72 * px.Green + 0.07 * px.Blue);
                    sum += luma;
                    sumSq += luma * luma;
                }
            }

            double mean = sum / (double)count;
            double variance = sumSq / (double)count - mean * mean;
            double stdDev = Math.Sqrt(Math.Max(0, variance));

            double brightness = Math.Clamp((128 - mean) / 128.0 * 40, -30, 30);
            double contrast = Math.Clamp((45 - stdDev) / 45.0 * 30, -10, 30);
            return (Brightness: brightness, Contrast: contrast, Saturation: 10.0);
        });

        // Stale if another edit (a slider settling, Reset, or a second AutoEnhance click)
        // already bumped the version while the histogram scan was running.
        if (enhancement == default || token != img.AdjustmentVersion) return;

        img.Brightness = enhancement.Brightness;
        img.Contrast = enhancement.Contrast;
        img.Saturation = enhancement.Saturation;
        ImgBrightness = img.Brightness; ImgContrast = img.Contrast; ImgSaturation = img.Saturation;

        var after = SnapshotAdjustments(img);
        if (!_isApplyingHistory && !before.Equals(after))
        {
            PushAdjustmentCommand(img, before, after);
        }
        await RecomputeImageAdjustmentsAsync(img);
    }

    [RelayCommand]
    private void ResetAdjustments()
    {
        if (SelectedElement is not ImageElement img) return;
        CancelPendingAdjustmentDebounce();
        var before = SnapshotAdjustments(img);

        img.Brightness = 0; img.Contrast = 0; img.Saturation = 0;
        img.Hue = 0; img.Temperature = 0; img.Tint = 0;
        img.BlurRadius = 0; img.SharpenAmount = 0;
        img.ActiveFilterPreset = null;
        img.AdjustmentVersion++; // invalidate any in-flight recompute for the pre-reset state
        img.AdjustedSource?.Dispose();
        img.AdjustedSource = null;
        SyncAdjustmentSlidersToSelection();

        if (!_isApplyingHistory)
        {
            PushAdjustmentCommand(img, before, SnapshotAdjustments(img));
        }
        RefreshCanvas();
    }

    /// <summary>Cancels a pending debounced image-adjustment commit so it can never fire later
    /// with a stale "before" snapshot (e.g. right after Undo/Redo/Reset/AutoEnhance).</summary>
    private void CancelPendingAdjustmentDebounce()
    {
        _adjustmentDebounce.Stop();
        _adjustmentDebounce.Tick -= OnAdjustmentDebounceTick;
        _pendingAdjustmentTarget = null;
        _adjustmentUndoSnapshot = null;
    }

    private static ImageAdjustmentSnapshot SnapshotAdjustments(ImageElement img) => new(
        img.Brightness, img.Contrast, img.Saturation, img.Hue, img.Temperature, img.Tint,
        img.ActiveFilterPreset, img.BlurRadius, img.SharpenAmount);

    private void QueueAdjustmentRecompute(Action<ImageElement> apply)
    {
        if (SelectedElement is not ImageElement img) return;

        // Snapshot the state before THIS gesture started (first change since the debounce
        // last settled), so the whole slider-drag becomes one undo step, not one per tick.
        _adjustmentUndoSnapshot ??= SnapshotAdjustments(img);
        _pendingAdjustmentTarget = img;
        apply(img);

        _adjustmentDebounce.Stop();
        _adjustmentDebounce.Tick -= OnAdjustmentDebounceTick;
        _adjustmentDebounce.Tick += OnAdjustmentDebounceTick;
        _adjustmentDebounce.Start();
    }

    private void OnAdjustmentDebounceTick(object? sender, EventArgs e)
    {
        _adjustmentDebounce.Stop();
        _adjustmentDebounce.Tick -= OnAdjustmentDebounceTick;

        var target = _pendingAdjustmentTarget;
        var snapshot = _adjustmentUndoSnapshot;
        _pendingAdjustmentTarget = null;
        _adjustmentUndoSnapshot = null;

        if (target == null) return;

        var after = SnapshotAdjustments(target);
        if (!_isApplyingHistory && snapshot is { } before && !before.Equals(after))
        {
            PushAdjustmentCommand(target, before, after);
        }

        _ = RecomputeImageAdjustmentsAsync(target);
    }

    private void PushAdjustmentCommand(
        ImageElement element,
        ImageAdjustmentSnapshot before,
        ImageAdjustmentSnapshot after)
    {
        History.Push(new GenericPropertyCommand<ImageElement, ImageAdjustmentSnapshot>(
            element,
            (e, v) =>
            {
                e.Brightness = v.Brightness; e.Contrast = v.Contrast; e.Saturation = v.Saturation;
                e.Hue = v.Hue; e.Temperature = v.Temperature; e.Tint = v.Tint;
                e.BlurRadius = v.BlurRadius; e.SharpenAmount = v.SharpenAmount;
                e.ActiveFilterPreset = v.Preset;
                _ = RecomputeImageAdjustmentsAsync(e);
                if (SelectedElement == e) SyncAdjustmentSlidersToSelection();
            },
            before, after, "Adjust image"));
    }

    private async Task RecomputeImageAdjustmentsAsync(ImageElement element)
    {
        if (element.OriginalImageBytes is not { Length: > 0 } bytes) return;

        int token = ++element.AdjustmentVersion;
        var matrix = ImageAdjustmentProcessor.BuildMatrix(
            element.Brightness, element.Contrast, element.Saturation,
            element.Hue, element.Temperature, element.Tint);
        if (element.ActiveFilterPreset != null)
        {
            matrix = ImageAdjustmentProcessor.Compose(
                ImageAdjustmentProcessor.BuildPresetMatrix(element.ActiveFilterPreset), matrix);
        }
        using var spatialFilter = ImageAdjustmentProcessor.BuildSpatialFilter(element.BlurRadius, element.SharpenAmount);

        bool isIdentity = element.Brightness == 0 && element.Contrast == 0 && element.Saturation == 0
                           && element.Hue == 0 && element.Temperature == 0 && element.Tint == 0
                           && element.BlurRadius == 0 && element.SharpenAmount == 0
                           && element.ActiveFilterPreset == null;
        if (isIdentity)
        {
            if (token != element.AdjustmentVersion) return;
            element.AdjustedSource?.Dispose();
            element.AdjustedSource = null;
            RefreshCanvas();
            return;
        }

        Bitmap? result = await Task.Run(() =>
        {
            using var src = SKBitmap.Decode(bytes);
            if (src is null) return null;

            using var target = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(target))
            using (var colorFilter = SKColorFilter.CreateColorMatrix(matrix))
            using (var paint = new SKPaint
            {
                ColorFilter = colorFilter,
                ImageFilter = spatialFilter,
            })
            {
                canvas.DrawBitmap(src, 0, 0, paint);
            }

            using var image = SKImage.FromBitmap(target);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(data.ToArray());
            return new Bitmap(ms);
        });

        if (result is null || token != element.AdjustmentVersion)
        {
            result?.Dispose();
            return;
        }

        element.AdjustedSource?.Dispose();
        element.AdjustedSource = result;
        RefreshCanvas();
    }

    // ── Rotate / Flip / Aspect lock (single-element transforms) ─────────────
    [RelayCommand]
    private void RotateClockwise() => MutateSelected(el =>
        el.RotationDegrees = (el.RotationDegrees + 90) % 360, "Rotate");

    [RelayCommand]
    private void RotateCounterClockwise() => MutateSelected(el =>
        el.RotationDegrees = (el.RotationDegrees - 90 + 360) % 360, "Rotate");

    [RelayCommand]
    private void FlipHorizontal() => MutateSelected(el => el.FlipX = !el.FlipX, "Flip horizontal");

    [RelayCommand]
    private void FlipVertical() => MutateSelected(el => el.FlipY = !el.FlipY, "Flip vertical");

    private void MutateSelected(Action<CanvasElement> apply, string description)
    {
        if (SelectedElement is not { } el) return;

        double oldRotation = el.RotationDegrees;
        bool oldFlipX = el.FlipX, oldFlipY = el.FlipY;
        apply(el);

        if (!_isApplyingHistory)
        {
            var newState = (el.RotationDegrees, el.FlipX, el.FlipY);
            History.Push(new GenericPropertyCommand<CanvasElement, (double R, bool FX, bool FY)>(
                el,
                (target, v) => { target.RotationDegrees = v.R; target.FlipX = v.FX; target.FlipY = v.FY; },
                (oldRotation, oldFlipX, oldFlipY), newState, description));
        }
        RefreshCanvas();
        SyncStyleFieldsToSelection();
    }

    // ── Element style/transform properties (Properties tab — fill/stroke/opacity/geometry,
    // works for any element type via the IHasFill/IHasStroke interfaces) ────────────────
    private bool _isSyncingStyleFields;
    private readonly DispatcherTimer _styleDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private Func<IUndoableCommand>? _pendingStyleCommandFactory;

    [ObservableProperty] private Color _fillColorField = Colors.Transparent;
    [ObservableProperty] private bool _hasGradientField;
    [ObservableProperty] private string _gradientKindField = "Linear";
    [ObservableProperty] private double _gradientAngleField = 45;
    [ObservableProperty] private Color _gradientStartColorField = Colors.White;
    [ObservableProperty] private Color _gradientEndColorField = Colors.Black;
    [ObservableProperty] private Color _strokeColorField = Colors.Transparent;
    [ObservableProperty] private double _strokeThicknessField;
    [ObservableProperty] private StrokeDashStyle _dashStyleField = StrokeDashStyle.Solid;
    [ObservableProperty] private double _cornerRadiusField;
    [ObservableProperty] private Color _textForegroundColorField = Colors.Black;
    [ObservableProperty] private double _opacityField = 100;
    [ObservableProperty] private double _elementXField;
    [ObservableProperty] private double _elementYField;
    [ObservableProperty] private double _elementWidthField;
    [ObservableProperty] private double _elementHeightField;
    [ObservableProperty] private double _elementRotationField;
    [ObservableProperty] private bool _aspectLockedField;

    /// <summary>Refreshes the Properties-tab fields to match the newly-selected element,
    /// without treating the refresh itself as a user edit.</summary>
    private void SyncStyleFieldsToSelection()
    {
        _isSyncingStyleFields = true;
        try
        {
            if (SelectedElement is { } el)
            {
                if (el is IHasFill fillable)
                {
                    FillColorField = fillable.FillColor;
                    if (fillable.Gradient is { } g)
                    {
                        HasGradientField = true;
                        GradientKindField = g.Kind.ToString();
                        GradientAngleField = g.AngleDegrees;
                        GradientStartColorField = g.Stops.Count > 0 ? g.Stops[0].Color : Colors.White;
                        GradientEndColorField = g.Stops.Count > 1 ? g.Stops[^1].Color : Colors.Black;
                    }
                    else
                    {
                        HasGradientField = false;
                    }
                }
                if (el is IHasStroke strokeable)
                {
                    StrokeColorField = strokeable.StrokeColor;
                    StrokeThicknessField = strokeable.StrokeThickness;
                    DashStyleField = strokeable.DashStyle;
                }
                if (el is RectangleElement rect) CornerRadiusField = rect.CornerRadius;
                if (el is TextElement text)
                {
                    TextForegroundColorField = text.ForegroundColor;
                    FontSize = text.FontSize;
                    IsBold = text.FontWeight == FontWeight.Bold;
                    IsItalic = text.FontStyle == FontStyle.Italic;
                }
                OpacityField = el.Opacity * 100;
                ElementXField = el.X;
                ElementYField = el.Y;
                ElementWidthField = el.Width;
                ElementHeightField = el.Height;
                ElementRotationField = el.RotationDegrees;
                AspectLockedField = el.AspectLocked;
            }
        }
        finally { _isSyncingStyleFields = false; }
    }

    /// <summary>Applies a style-property change immediately (so drags/pickers feel live) and,
    /// after a short settle window, pushes ONE combined undo command for the whole gesture —
    /// generalizes the image-adjustment debounce pattern to any property/target pair.</summary>
    private void ApplyStyleField<TTarget, TValue>(TTarget target, TValue before, TValue after, Action<TTarget, TValue> setter, string description)
    {
        setter(target, after);
        RefreshCanvas();
        // The setter may itself have silently changed OTHER model properties (e.g. TextElement's
        // FontSize setter re-measuring Width/Height) — resync so those fields never go stale.
        if (ReferenceEquals(SelectedElement, target)) SyncStyleFieldsToSelection();
        if (_isApplyingHistory) return;

        _pendingStyleCommandFactory ??= () => new GenericPropertyCommand<TTarget, TValue>(
            target,
            (t, v) => { setter(t, v); if (ReferenceEquals(SelectedElement, t)) SyncStyleFieldsToSelection(); },
            before, after, description);

        _styleDebounce.Stop();
        _styleDebounce.Tick -= OnStyleDebounceTick;
        _styleDebounce.Tick += OnStyleDebounceTick;
        _styleDebounce.Start();
    }

    private void OnStyleDebounceTick(object? sender, EventArgs e)
    {
        _styleDebounce.Stop();
        _styleDebounce.Tick -= OnStyleDebounceTick;
        var factory = _pendingStyleCommandFactory;
        _pendingStyleCommandFactory = null;
        if (factory == null || _isApplyingHistory) return;
        History.Push(factory());
    }

    /// <summary>Cancels a pending debounced style-field commit (fill/stroke/opacity/geometry/
    /// font edits) so it can never fire later with a stale "before" snapshot.</summary>
    private void CancelPendingStyleDebounce()
    {
        _styleDebounce.Stop();
        _styleDebounce.Tick -= OnStyleDebounceTick;
        _pendingStyleCommandFactory = null;
    }

    partial void OnFillColorFieldChanged(Color value)
    {
        if (_isSyncingStyleFields || SelectedFillable is not { } target) return;
        ApplyStyleField(target, target.FillColor, value, (t, v) => t.FillColor = v, "Change fill color");
    }

    /// <summary>Gradient is additive to FillColor, not a replacement — Draw() already prefers
    /// Gradient when set. Every edit swaps the whole immutable GradientFill object, so a plain
    /// GenericPropertyCommand (via ApplyStyleField) already covers undo with no new command type.</summary>
    [RelayCommand]
    private void ToggleGradientFill()
    {
        if (SelectedFillable is not { } target) return;
        var before = target.Gradient;
        GradientFill? after = before == null
            ? GradientFill.CreateDefault(Enum.Parse<GradientKind>(GradientKindField), GradientStartColorField, GradientEndColorField)
            : null;

        HasGradientField = after != null;
        if (after != null) GradientAngleField = after.AngleDegrees;

        ApplyStyleField(target, before, after, (t, v) => t.Gradient = v, "Toggle gradient fill");
    }

    [RelayCommand]
    private void SetGradientKind(string kind)
    {
        if (_isSyncingStyleFields || GradientKindField == kind) return;
        GradientKindField = kind;
        RebuildAndApplyGradient();
    }

    partial void OnGradientAngleFieldChanged(double value) { if (!_isSyncingStyleFields) RebuildAndApplyGradient(); }
    partial void OnGradientStartColorFieldChanged(Color value) { if (!_isSyncingStyleFields) RebuildAndApplyGradient(); }
    partial void OnGradientEndColorFieldChanged(Color value) { if (!_isSyncingStyleFields) RebuildAndApplyGradient(); }

    private void RebuildAndApplyGradient()
    {
        if (SelectedFillable is not { } target || !HasGradientField) return;
        var before = target.Gradient;
        var after = new GradientFill
        {
            Kind = Enum.Parse<GradientKind>(GradientKindField),
            AngleDegrees = GradientAngleField,
            Stops = [new GradientStopValue(0, GradientStartColorField), new GradientStopValue(1, GradientEndColorField)],
        };
        ApplyStyleField(target, before, after, (t, v) => t.Gradient = v, "Edit gradient fill");
    }

    partial void OnStrokeColorFieldChanged(Color value)
    {
        if (_isSyncingStyleFields || SelectedStrokeable is not { } target) return;
        ApplyStyleField(target, target.StrokeColor, value, (t, v) => t.StrokeColor = v, "Change stroke color");
    }

    partial void OnStrokeThicknessFieldChanged(double value)
    {
        if (_isSyncingStyleFields || SelectedStrokeable is not { } target) return;
        ApplyStyleField(target, target.StrokeThickness, Math.Max(0, value), (t, v) => t.StrokeThickness = v, "Change stroke width");
    }

    partial void OnCornerRadiusFieldChanged(double value)
    {
        if (_isSyncingStyleFields || SelectedRectangleElement is not { } target) return;
        ApplyStyleField(target, target.CornerRadius, Math.Max(0, value), (t, v) => t.CornerRadius = v, "Change corner radius");
    }

    partial void OnTextForegroundColorFieldChanged(Color value)
    {
        if (_isSyncingStyleFields || SelectedTextElement is not { } target) return;
        ApplyStyleField(target, target.ForegroundColor, value, (t, v) => t.ForegroundColor = v, "Change text color");
    }

    partial void OnOpacityFieldChanged(double value)
    {
        if (_isSyncingStyleFields || SelectedElement is not { } target) return;
        ApplyStyleField(target, target.Opacity, Math.Clamp(value, 0, 100) / 100.0, (t, v) => t.Opacity = v, "Change opacity");
    }

    partial void OnElementXFieldChanged(double value)
    {
        if (_isSyncingStyleFields || SelectedElement is not { } target) return;
        ApplyStyleField(target, target.X, value, (t, v) => t.X = v, "Move element");
    }

    partial void OnElementYFieldChanged(double value)
    {
        if (_isSyncingStyleFields || SelectedElement is not { } target) return;
        ApplyStyleField(target, target.Y, value, (t, v) => t.Y = v, "Move element");
    }

    partial void OnElementWidthFieldChanged(double value)
    {
        if (_isSyncingStyleFields || SelectedElement is not { } target) return;
        // Typing a width directly is a manual resize too — mirrors EditorCanvasControl's
        // handle-drag guard, or the very next FontSize/Text edit would silently revert it.
        if (target is TextElement textEl) textEl.IsWidthAutoFitEnabled = false;
        ApplyStyleField(target, target.Width, Math.Max(1, value), (t, v) => t.Width = v, "Resize element");
    }

    partial void OnElementHeightFieldChanged(double value)
    {
        if (_isSyncingStyleFields || SelectedElement is not { } target) return;
        if (target is TextElement textEl) textEl.IsHeightAutoFitEnabled = false;
        ApplyStyleField(target, target.Height, Math.Max(1, value), (t, v) => t.Height = v, "Resize element");
    }

    partial void OnElementRotationFieldChanged(double value)
    {
        if (_isSyncingStyleFields || SelectedElement is not { } target) return;
        double normalized = ((value % 360) + 360) % 360;
        ApplyStyleField(target, target.RotationDegrees, normalized, (t, v) => t.RotationDegrees = v, "Rotate element");
    }

    partial void OnAspectLockedFieldChanged(bool value)
    {
        if (_isSyncingStyleFields || SelectedElement is not { } target) return;
        ApplyStyleField(target, target.AspectLocked, value, (t, v) => t.AspectLocked = v, "Toggle aspect lock");
    }

    partial void OnFontSizeChanged(double value)
    {
        if (_isSyncingStyleFields || SelectedTextElement is not { } target) return;
        ApplyStyleField(target, target.FontSize, value, (t, v) => t.FontSize = v, "Change font size");
    }

    partial void OnIsBoldChanged(bool value)
    {
        if (_isSyncingStyleFields || SelectedTextElement is not { } target) return;
        var newWeight = value ? FontWeight.Bold : FontWeight.Normal;
        ApplyStyleField(target, target.FontWeight, newWeight, (t, v) => t.FontWeight = v, "Toggle bold");
    }

    partial void OnIsItalicChanged(bool value)
    {
        if (_isSyncingStyleFields || SelectedTextElement is not { } target) return;
        var newStyle = value ? FontStyle.Italic : FontStyle.Normal;
        ApplyStyleField(target, target.FontStyle, newStyle, (t, v) => t.FontStyle = v, "Toggle italic");
    }

    [RelayCommand]
    private void SetDashStyle(string styleName)
    {
        if (SelectedStrokeable is not { } target || !Enum.TryParse<StrokeDashStyle>(styleName, out var style)) return;
        if (target.DashStyle == style) return;
        var before = target.DashStyle;
        target.DashStyle = style;
        DashStyleField = style;
        RefreshCanvas();
        if (!_isApplyingHistory)
        {
            History.Push(new GenericPropertyCommand<IHasStroke, StrokeDashStyle>(
                target, (t, v) => { t.DashStyle = v; if (ReferenceEquals(SelectedElement, t)) SyncStyleFieldsToSelection(); },
                before, style, "Change stroke style"));
        }
    }

    // ── Duplicate / Copy / Paste (in-app element clipboard) ─────────────────
    private List<CanvasElement>? _elementClipboard;

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedElements.Count == 0) return;
        AddClonesAndSelect(CloneWithOffset(SelectedElements), "Duplicated");
    }

    [RelayCommand]
    private void CopyElements()
    {
        if (SelectedElements.Count == 0) return;
        _elementClipboard = SelectedElements.Select(e => e.Clone()).ToList();
        StatusMessage = $"Copied {_elementClipboard.Count} element(s)";
    }

    [RelayCommand]
    private void PasteElements()
    {
        if (_elementClipboard is not { Count: > 0 } clipboard) return;
        AddClonesAndSelect(CloneWithOffset(clipboard), "Pasted");
    }

    private List<CanvasElement> CloneWithOffset(IEnumerable<CanvasElement> source)
    {
        int nextZ = (Elements.Count > 0 ? Elements.Max(e => e.ZIndex) : -1) + 1;
        var clones = new List<CanvasElement>();
        foreach (var e in source.OrderBy(e => e.ZIndex))
        {
            var c = e.Clone();
            c.X += 20; c.Y += 20; c.GroupId = null; c.ZIndex = nextZ++;
            clones.Add(c);
        }
        return clones;
    }

    private void AddClonesAndSelect(List<CanvasElement> clones, string statusVerb)
    {
        if (clones.Count == 0) return;
        foreach (var c in clones) Elements.Add(c);
        if (!_isApplyingHistory) History.Push(new AddMultipleElementsCommand(Elements, clones));

        // Clone() never bakes AdjustedSource itself — re-trigger the bake for any duplicated/
        // pasted image that has non-default adjustments, or it would render unedited until
        // the user nudges a slider.
        foreach (var img in clones.OfType<ImageElement>().Where(HasNonDefaultAdjustments))
        {
            _ = RecomputeImageAdjustmentsAsync(img);
        }

        UpdateSelectionFromCanvas(clones);
        CanvasControl?.SetSelection(clones);
        RefreshCanvas();
        StatusMessage = $"{statusVerb} {clones.Count} element(s)";
    }

    // ── Zoom / pan ─────────────────────────────────────────────────────────
    [ObservableProperty]
    private double _zoomLevel = 1.0;

    public string ZoomPercentageLabel => $"{(int)Math.Round(ZoomLevel * 100)}%";

    partial void OnZoomLevelChanged(double value) => OnPropertyChanged(nameof(ZoomPercentageLabel));

    [RelayCommand]
    private void ZoomIn() => CanvasControl?.ZoomBy(1.2);

    [RelayCommand]
    private void ZoomOut() => CanvasControl?.ZoomBy(1 / 1.2);

    [RelayCommand]
    private void ResetZoomToActual() => CanvasControl?.ResetZoomToActual();

    [RelayCommand]
    private void FitCanvasToWindow() => CanvasControl?.FitToWindow();

    // ── Constructor ────────────────────────────────────────────────────────
    public ImageEditorViewModel(IServiceProvider? serviceProvider = null)
    {
        _settingsStore = serviceProvider?.GetService<IPluginSettingsStore>();
        History.HistoryChanged += (_, _) =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };
        Elements.CollectionChanged += (_, _) => OnPropertyChanged(nameof(LayersDescendingZ));
        LoadSettings();
        AddSampleElements();
    }

    private void LoadSettings()
    {
        if (_settingsStore == null) return;

        try
        {
            var bg = _settingsStore.GetSetting(PluginId, "DefaultBackground", "White");
            SetBackgroundByName(bg);

            _canvasWidth = _settingsStore.GetSetting(PluginId, "DefaultCanvasWidth", 640.0);
            _canvasHeight = _settingsStore.GetSetting(PluginId, "DefaultCanvasHeight", 480.0);
            CanvasSizeLabel = $"{(int)_canvasWidth} × {(int)_canvasHeight}";
        }
        catch (Exception ex)
        {
            // A malformed stored setting must never leave the overlay silently blank —
            // fall back to safe defaults and surface why.
            SetBackgroundByName("White");
            _canvasWidth = 640;
            _canvasHeight = 480;
            CanvasSizeLabel = "640 × 480";
            StatusMessage = "Using default settings (stored settings could not be read).";
            System.Diagnostics.Debug.WriteLine($"[ImageEditor] LoadSettings error: {ex}");
        }
    }

    private void SetBackgroundByName(string name)
    {
        BackgroundColorName = name;
        BackgroundColor = name switch
        {
            "Black" => Colors.Black,
            "Dark" => Color.FromRgb(0x18, 0x1E, 0x2A),
            "Gray" => Color.FromRgb(0xEA, 0xEE, 0xF4),
            "Indigo" => Color.FromRgb(0x31, 0x2E, 0x81),
            "Transparent" => Colors.Transparent,
            _ => Colors.White
        };
    }

    [RelayCommand]
    private void ToggleOrientation()
    {
        var newW = _canvasHeight;
        var newH = _canvasWidth;
        _canvasWidth = newW;
        _canvasHeight = newH;
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        CanvasSizeLabel = $"{(int)_canvasWidth} × {(int)_canvasHeight}";
        CanvasControl?.SetLogicalCanvasSize(_canvasWidth, _canvasHeight);
        CanvasControl?.FitToWindow();
        StatusMessage = $"Swapped canvas orientation to {CanvasSizeLabel}.";
    }

    /// <summary>Add a few sample elements to give a good first impression.</summary>
    private void AddSampleElements()
    {
        var rect = new RectangleElement
        {
            X = 60, Y = 80, Width = 160, Height = 100,
            FillColor = Color.FromArgb(160, 99, 102, 241),
            ZIndex = 1
        };
        var ellipse = new EllipseElement
        {
            X = 280, Y = 100, Width = 110, Height = 110,
            FillColor = Color.FromArgb(160, 236, 72, 153),
            ZIndex = 2
        };
        var text = new TextElement
        {
            X = 80, Y = 240, Text = "Image Editor 🎨",
            ForegroundColor = Color.FromArgb(255, 30, 30, 30),
            FontSize = 28,
            FontWeight = FontWeight.Bold,
            ZIndex = 3
        };
        var arrow = new ArrowElement
        {
            X = 420, Y = 120, Width = 80, Height = 60,
            StrokeColor = Color.FromArgb(255, 251, 191, 36),
            StrokeThickness = 3,
            ZIndex = 4
        };

        Elements.Add(rect);
        Elements.Add(ellipse);
        Elements.Add(text);
        Elements.Add(arrow);
    }

    // ── Tool Selection ─────────────────────────────────────────────────────
    [RelayCommand]
    private void SetTool(string tool) => ActiveToolMode = tool;

    // ── Element placement (click-to-place, driven by EditorCanvasControl's tool-placement
    //    events — see PlaceTextRequested/ShapePlaced wired in ImageEditorView.axaml.cs) ────
    private void CommitNewElement(CanvasElement el)
    {
        Elements.Add(el);
        SelectedElement = el;
        ActiveToolMode = "select";
        RefreshCanvas();
        if (!_isApplyingHistory) History.Push(new AddElementCommand(Elements, el));
    }

    public void PlaceTextElement(Point at)
    {
        var el = new TextElement
        {
            X = at.X - 80,
            Y = at.Y - 20,
            Text = string.IsNullOrWhiteSpace(NewTextContent) ? "Text" : NewTextContent,
            ForegroundColor = PrimaryColor,
            FontSize = FontSize,
            FontWeight = IsBold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = IsItalic ? FontStyle.Italic : FontStyle.Normal,
            ZIndex = Elements.Count + 1
        };
        CommitNewElement(el);
    }

    /// <summary>toolType is "rect" | "ellipse" | "arrow". start==end means the user clicked
    /// rather than dragged — falls back to a sensible default size centered on the click.</summary>
    public void PlaceShapeElement(string toolType, Point start, Point end)
    {
        bool isClick = start == end;
        CanvasElement el = toolType switch
        {
            "ellipse" => isClick
                ? new EllipseElement { X = start.X - 50, Y = start.Y - 50, Width = 100, Height = 100 }
                : new EllipseElement
                {
                    X = Math.Min(start.X, end.X), Y = Math.Min(start.Y, end.Y),
                    Width = Math.Max(Math.Abs(end.X - start.X), 20),
                    Height = Math.Max(Math.Abs(end.Y - start.Y), 20)
                },
            "arrow" => isClick
                ? new ArrowElement { X = start.X - 60, Y = start.Y, Width = 120, Height = 0 }
                : new ArrowElement { X = start.X, Y = start.Y, Width = end.X - start.X, Height = end.Y - start.Y },
            _ => isClick
                ? new RectangleElement { X = start.X - 60, Y = start.Y - 40, Width = 120, Height = 80 }
                : new RectangleElement
                {
                    X = Math.Min(start.X, end.X), Y = Math.Min(start.Y, end.Y),
                    Width = Math.Max(Math.Abs(end.X - start.X), 20),
                    Height = Math.Max(Math.Abs(end.Y - start.Y), 20)
                }
        };

        el.ZIndex = Elements.Count + 1;
        switch (el)
        {
            case RectangleElement r:
                r.FillColor = Color.FromArgb(180, PrimaryColor.R, PrimaryColor.G, PrimaryColor.B);
                r.StrokeColor = PrimaryColor;
                break;
            case EllipseElement e:
                e.FillColor = Color.FromArgb(180, PrimaryColor.R, PrimaryColor.G, PrimaryColor.B);
                e.StrokeColor = PrimaryColor;
                break;
            case ArrowElement a:
                a.StrokeColor = PrimaryColor;
                a.StrokeThickness = 2.5;
                break;
        }

        CommitNewElement(el);
    }

    [RelayCommand]
    private async Task ImportImageAsync()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel?.StorageProvider is not { } storage) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Image",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"],
                        MimeTypes = ["image/*"]
                    }
                ]
            });

            if (files.Count == 0) return;
            var file = files[0];

            byte[] originalBytes;
            await using (var readStream = await file.OpenReadAsync())
            using (var ms = new MemoryStream())
            {
                await readStream.CopyToAsync(ms);
                originalBytes = ms.ToArray();
            }

            // Decode off the UI thread — a large imported photo's decode would otherwise
            // freeze the whole editor for its duration.
            var (bitmap, naturalWidth, naturalHeight) = await Task.Run(() =>
            {
                using var bitmapStream = new MemoryStream(originalBytes);
                var bmp = new Bitmap(bitmapStream);
                return (bmp, bmp.PixelSize.Width, bmp.PixelSize.Height);
            });

            // Scale down (never up) to fit a 300x225 box while preserving aspect ratio —
            // clamping width/height independently would distort non-4:3 images on import.
            double fitScale = Math.Min(1.0, Math.Min(300.0 / naturalWidth, 225.0 / naturalHeight));

            var el = new ImageElement
            {
                Source = bitmap,
                FilePath = file.Path.LocalPath,
                NaturalPixelWidth = naturalWidth,
                NaturalPixelHeight = naturalHeight,
                OriginalImageBytes = originalBytes,
                X = _canvasWidth / 2 - (naturalWidth * fitScale) / 2,
                Y = _canvasHeight / 2 - (naturalHeight * fitScale) / 2,
                Width = naturalWidth * fitScale,
                Height = naturalHeight * fitScale,
                ZIndex = Elements.Count + 1
            };

            Elements.Add(el);
            SelectedElement = el;
            ActiveToolMode = "select";
            RefreshCanvas();
            if (!_isApplyingHistory) History.Push(new AddElementCommand(Elements, el));
            StatusMessage = $"Imported {file.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ImageEditor] Import error: {ex}");
        }
    }

    // ── Selection & Z-Order ────────────────────────────────────────────────
    [RelayCommand]
    private void DeleteSelectedElement()
    {
        if (SelectedElements.Count == 0) return;
        var removed = SelectedElements.ToList();
        var command = _isApplyingHistory ? null : new RemoveElementsCommand(Elements, removed);
        foreach (var el in removed) Elements.Remove(el);
        SelectedElement = null;
        SelectedElements.Clear();
        CanvasControl?.SetSelected(null);
        RefreshCanvas();
        if (command != null) History.Push(command);
    }

    [RelayCommand]
    private void BringForward() => ShiftZOrder(forward: true);

    [RelayCommand]
    private void SendBackward() => ShiftZOrder(forward: false);

    /// <summary>Moves the whole selected block past exactly one non-selected neighbor —
    /// generalizes the old single-element swap to work for any multi-selection.</summary>
    private void ShiftZOrder(bool forward)
    {
        if (SelectedElements.Count == 0) return;

        var ordered = Elements.OrderBy(e => e.ZIndex).ToList();
        var before = ordered.ToDictionary(e => e, e => e.ZIndex);
        var selectedSet = new HashSet<CanvasElement>(SelectedElements);

        if (forward)
        {
            for (int i = ordered.Count - 2; i >= 0; i--)
            {
                if (selectedSet.Contains(ordered[i]) && !selectedSet.Contains(ordered[i + 1]))
                {
                    (ordered[i], ordered[i + 1]) = (ordered[i + 1], ordered[i]);
                }
            }
        }
        else
        {
            for (int i = 1; i < ordered.Count; i++)
            {
                if (selectedSet.Contains(ordered[i]) && !selectedSet.Contains(ordered[i - 1]))
                {
                    (ordered[i], ordered[i - 1]) = (ordered[i - 1], ordered[i]);
                }
            }
        }

        for (int i = 0; i < ordered.Count; i++) ordered[i].ZIndex = i;
        var after = ordered.ToDictionary(e => e, e => e.ZIndex);

        RefreshCanvas();
        OnPropertyChanged(nameof(LayersDescendingZ));
        if (!_isApplyingHistory) History.Push(new ZOrderShiftCommand(before, after));
    }

    // ── Alignment / Distribution (2+/3+ selection) ──────────────────────────
    [RelayCommand]
    private void AlignLeft() => AlignSelection(el => el.X = ComputeGroupBounds().X);

    [RelayCommand]
    private void AlignRight() => AlignSelection(el => el.X = ComputeGroupBounds().Right - el.Width);

    [RelayCommand]
    private void AlignTop() => AlignSelection(el => el.Y = ComputeGroupBounds().Y);

    [RelayCommand]
    private void AlignBottom() => AlignSelection(el => el.Y = ComputeGroupBounds().Bottom - el.Height);

    [RelayCommand]
    private void AlignCenterHorizontal()
    {
        double centerX = ComputeGroupBounds().Center.X;
        AlignSelection(el => el.X = centerX - el.Width / 2);
    }

    [RelayCommand]
    private void AlignCenterVertical()
    {
        double centerY = ComputeGroupBounds().Center.Y;
        AlignSelection(el => el.Y = centerY - el.Height / 2);
    }

    [RelayCommand]
    private void DistributeHorizontal()
    {
        if (SelectedElements.Count < 3) return;
        var sorted = SelectedElements.OrderBy(e => e.X).ToList();
        var before = sorted.ToDictionary(e => e, e => new Point(e.X, e.Y));

        double span = (sorted[^1].X + sorted[^1].Width) - sorted[0].X;
        double totalWidth = sorted.Sum(e => e.Width);
        double gap = (span - totalWidth) / (sorted.Count - 1);
        double cursor = sorted[0].X + sorted[0].Width;
        for (int i = 1; i < sorted.Count - 1; i++)
        {
            cursor += gap;
            sorted[i].X = cursor;
            cursor += sorted[i].Width;
        }
        PushMoveCommand(before);
    }

    [RelayCommand]
    private void DistributeVertical()
    {
        if (SelectedElements.Count < 3) return;
        var sorted = SelectedElements.OrderBy(e => e.Y).ToList();
        var before = sorted.ToDictionary(e => e, e => new Point(e.X, e.Y));

        double span = (sorted[^1].Y + sorted[^1].Height) - sorted[0].Y;
        double totalHeight = sorted.Sum(e => e.Height);
        double gap = (span - totalHeight) / (sorted.Count - 1);
        double cursor = sorted[0].Y + sorted[0].Height;
        for (int i = 1; i < sorted.Count - 1; i++)
        {
            cursor += gap;
            sorted[i].Y = cursor;
            cursor += sorted[i].Height;
        }
        PushMoveCommand(before);
    }

    private Rect ComputeGroupBounds()
    {
        if (SelectedElements.Count == 0) return default;
        var result = SelectedElements[0].Bounds;
        for (int i = 1; i < SelectedElements.Count; i++) result = result.Union(SelectedElements[i].Bounds);
        return result;
    }

    private void AlignSelection(Action<CanvasElement> apply)
    {
        if (SelectedElements.Count < 2) return;
        var before = SelectedElements.ToDictionary(e => e, e => new Point(e.X, e.Y));
        foreach (var el in SelectedElements) apply(el);
        PushMoveCommand(before);
    }

    private void PushMoveCommand(Dictionary<CanvasElement, Point> before)
    {
        RefreshCanvas();
        if (_isApplyingHistory) return;

        var moves = new List<(CanvasElement Element, Point OldPos, Point NewPos)>();
        foreach (var (el, oldPos) in before)
        {
            var newPos = new Point(el.X, el.Y);
            if (oldPos != newPos) moves.Add((el, oldPos, newPos));
        }
        if (moves.Count > 0) History.Push(new MoveElementsCommand(moves));
    }

    // ── Background ─────────────────────────────────────────────────────────
    [RelayCommand]
    private void SetBackground(string name)
    {
        if (name == BackgroundColorName) return;
        var oldName = BackgroundColorName;
        SetBackgroundByName(name);
        if (!_isApplyingHistory)
        {
            History.Push(new BackgroundChangeCommand(SetBackgroundByName, oldName, name));
        }
    }

    // ── Export ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task ExportToPngAsync()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel?.StorageProvider is not { } storage || CanvasControl == null) return;

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Canvas as PNG",
                SuggestedFileName = "canvas_export.png",
                DefaultExtension = ".png",
                FileTypeChoices =
                [
                    new FilePickerFileType("PNG Image") { Patterns = ["*.png"] }
                ]
            });

            if (file == null) return;

            using var bitmap = RenderCanvasToBitmap();
            if (bitmap == null) return;

            // Use Save(string path, BitmapEncoderOptions) — the current Avalonia 12.x API
            var tempPath = Path.ChangeExtension(Path.GetTempFileName(), ".png");
            bitmap.Save(tempPath, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

            await using var outStream = await file.OpenWriteAsync();
            var pngBytes = await File.ReadAllBytesAsync(tempPath);
            await outStream.WriteAsync(pngBytes);
            File.Delete(tempPath);
            StatusMessage = $"Exported to {file.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ImageEditor] Export error: {ex}");
        }
    }

    [RelayCommand]
    private async Task CopyToClipboardAsync()
    {
        Bitmap? bitmap = null;
        try
        {
            if (CanvasControl == null) return;
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                || desktop.MainWindow?.Clipboard is not { } clipboard)
            {
                return;
            }

            bitmap = RenderCanvasToBitmap();
            if (bitmap == null) return;

            try
            {
                // Real, first-class cross-platform clipboard image support in Avalonia 12.1.1 \u2014
                // RenderTargetBitmap derives from Bitmap, so no conversion is needed.
                await clipboard.SetBitmapAsync(bitmap);
                StatusMessage = "Canvas copied to clipboard as an image.";
            }
            catch (Exception)
            {
                // Defensive fallback for clipboard managers that don't support the bitmap
                // format (e.g. some Linux compositors) \u2014 offer a file reference instead.
                var tempPath = Path.ChangeExtension(Path.GetTempFileName(), ".png");
                bitmap.Save(tempPath, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

                var topLevel = GetTopLevel();
                var file = topLevel?.StorageProvider != null
                    ? await topLevel.StorageProvider.TryGetFileFromPathAsync(new Uri(tempPath))
                    : null;

                if (file != null)
                {
                    await clipboard.SetFileAsync(file);
                    StatusMessage = "Canvas copied to clipboard as a file reference.";
                }
                else
                {
                    await clipboard.SetTextAsync($"[FryPDF Image Editor Export saved to {tempPath}]");
                    StatusMessage = "Clipboard image copy unsupported here \u2014 path copied instead.";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Copy failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ImageEditor] Copy error: {ex}");
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    // ── Confirm-before-discard (Clear / Load Project) ───────────────────────
    // A small reusable in-view overlay rather than a new dialog subsystem. Only gates
    // destructive actions that would be hard/impossible to recover from — Clear is already
    // undoable via Ctrl+Z, but Load replaces the whole undo history too, so it genuinely can't be.
    [ObservableProperty]
    private bool _isConfirmingDiscard;

    [ObservableProperty]
    private string _discardConfirmMessage = string.Empty;

    private Action? _pendingDiscardAction;

    private void RequestDiscardConfirmation(string message, Action action)
    {
        if (Elements.Count == 0) { action(); return; }
        DiscardConfirmMessage = message;
        _pendingDiscardAction = action;
        IsConfirmingDiscard = true;
    }

    [RelayCommand]
    private void ConfirmDiscard()
    {
        IsConfirmingDiscard = false;
        var action = _pendingDiscardAction;
        _pendingDiscardAction = null;
        action?.Invoke();
    }

    [RelayCommand]
    private void CancelDiscard()
    {
        IsConfirmingDiscard = false;
        _pendingDiscardAction = null;
    }

    [RelayCommand]
    private void ClearCanvas() =>
        RequestDiscardConfirmation("Clear the canvas? You can undo this with Ctrl+Z.", ClearCanvasConfirmed);

    private void ClearCanvasConfirmed()
    {
        if (Elements.Count == 0) return;
        var command = _isApplyingHistory ? null : new ClearCanvasCommand(Elements, Elements);
        Elements.Clear();
        SelectedElement = null;
        CanvasControl?.SetSelected(null);
        RefreshCanvas();
        if (command != null) History.Push(command);
    }

    // ── Project save/load (.fryimg) ──────────────────────────────────────────
    private static readonly JsonSerializerOptions ProjectJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel?.StorageProvider is not { } storage) return;

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Project",
                SuggestedFileName = "MyDesign",
                DefaultExtension = ".fryimg",
                FileTypeChoices =
                [
                    new FilePickerFileType("FryPDF Image Editor Project") { Patterns = ["*.fryimg"] }
                ]
            });
            if (file == null) return;

            var dto = new ProjectFileDto
            {
                CanvasWidth = _canvasWidth,
                CanvasHeight = _canvasHeight,
                BackgroundColor = BackgroundColor.ToString(),
                Elements = Elements.OrderBy(e => e.ZIndex).Select(CanvasElementMapper.ToDto).ToList(),
            };

            string json = JsonSerializer.Serialize(dto, ProjectJsonOptions);
            await using var outStream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(outStream);
            await writer.WriteAsync(json);

            StatusMessage = $"Saved {file.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ImageEditor] Save error: {ex}");
        }
    }

    [RelayCommand]
    private async Task LoadProjectAsync()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel?.StorageProvider is not { } storage) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Project",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("FryPDF Image Editor Project") { Patterns = ["*.fryimg"] }
                ]
            });
            if (files.Count == 0) return;
            var file = files[0];

            string json;
            await using (var stream = await file.OpenReadAsync())
            using (var reader = new StreamReader(stream))
            {
                json = await reader.ReadToEndAsync();
            }

            var dto = JsonSerializer.Deserialize<ProjectFileDto>(json, ProjectJsonOptions);
            if (dto == null)
            {
                StatusMessage = "Load failed: the file isn't a valid project.";
                return;
            }

            RequestDiscardConfirmation(
                $"Loading \"{file.Name}\" will replace everything on the canvas. Continue?",
                () => ApplyProjectFile(dto, file.Name));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ImageEditor] Load error: {ex}");
        }
    }

    /// <summary>Clears and rebuilds Elements from a loaded project DTO, re-triggers the
    /// adjustment bake for any image with non-default adjustments, and clears undo history
    /// (a loaded project is a new baseline, not something to undo past).</summary>
    private void ApplyProjectFile(ProjectFileDto dto, string sourceName)
    {
        var elements = dto.Elements.OrderBy(e => e.ZIndex).Select(CanvasElementMapper.FromDto).ToList();
        ApplyCanvasState(dto.CanvasWidth, dto.CanvasHeight, Color.Parse(dto.BackgroundColor), elements, $"Loaded {sourceName}.");

        foreach (var img in Elements.OfType<ImageElement>().Where(HasNonDefaultAdjustments))
        {
            _ = RecomputeImageAdjustmentsAsync(img);
        }
    }

    /// <summary>Replaces the whole canvas (size, background, elements) — shared by project
    /// load and "Use Template". Clears undo history since the new baseline isn't something to
    /// undo past.</summary>
    private void ApplyCanvasState(double width, double height, Color background, IEnumerable<CanvasElement> elements, string statusMessage)
    {
        foreach (var img in Elements.OfType<ImageElement>())
        {
            img.Source?.Dispose();
            img.AdjustedSource?.Dispose();
        }
        Elements.Clear();
        SelectedElement = null;
        SelectedElements.Clear();
        CanvasControl?.SetSelected(null);

        _canvasWidth = width > 0 ? width : 640;
        _canvasHeight = height > 0 ? height : 480;
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        CanvasSizeLabel = $"{(int)_canvasWidth} × {(int)_canvasHeight}";
        CanvasControl?.SetLogicalCanvasSize(_canvasWidth, _canvasHeight);

        BackgroundColor = background;
        BackgroundColorName = background.A == 0 ? "Transparent" : background == Colors.Black ? "Black" : "White";

        foreach (var el in elements) Elements.Add(el);

        History.Clear();
        RefreshCanvas();
        StatusMessage = statusMessage;
    }

    private static bool HasNonDefaultAdjustments(ImageElement img) =>
        img.Brightness != 0 || img.Contrast != 0 || img.Saturation != 0
        || img.Hue != 0 || img.Temperature != 0 || img.Tint != 0
        || img.BlurRadius != 0 || img.SharpenAmount != 0
        || img.ActiveFilterPreset != null;

    // ── Template Gallery ─────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isTemplateGalleryOpen;

    [ObservableProperty]
    private string _selectedTemplateCategory = "All";

    public ObservableCollection<TemplateCardViewModel> TemplateCards { get; } = new();
    public ObservableCollection<TemplateCardViewModel> FilteredTemplateCards { get; } = new();

    [RelayCommand]
    private void FilterTemplatesByCategory(string category)
    {
        SelectedTemplateCategory = category;
        ApplyTemplateFilter();
    }

    private void ApplyTemplateFilter()
    {
        FilteredTemplateCards.Clear();
        foreach (var card in TemplateCards)
        {
            if (SelectedTemplateCategory == "All" || string.Equals(card.Category, SelectedTemplateCategory, StringComparison.OrdinalIgnoreCase))
            {
                FilteredTemplateCards.Add(card);
            }
        }
    }

    [RelayCommand]
    private void OpenTemplateGallery()
    {
        foreach (var card in TemplateCards) card.Preview?.Dispose();
        TemplateCards.Clear();
        FilteredTemplateCards.Clear();
        foreach (var def in TemplateLibrary.All)
        {
            TemplateCards.Add(new TemplateCardViewModel(def, RenderTemplatePreview(def)));
        }
        ApplyTemplateFilter();
        IsTemplateGalleryOpen = true;
    }

    [RelayCommand]
    private void CloseTemplateGallery() => IsTemplateGalleryOpen = false;

    [RelayCommand]
    private void UseTemplate(TemplateDefinition template)
    {
        IsTemplateGalleryOpen = false;
        RequestDiscardConfirmation(
            $"Using \"{template.Name}\" will replace everything on the canvas. Continue?",
            () => ApplyCanvasState(template.CanvasWidth, template.CanvasHeight, template.BackgroundColor,
                template.BuildElements(), $"Applied template: {template.Name}."));
    }

    /// <summary>Renders a template's elements into a small offscreen bitmap using the SAME
    /// rendering path as the live canvas (EditorCanvasControl.RenderElementsStatic), so the
    /// gallery preview always matches what "Use Template" actually produces — computed once
    /// per gallery-open, not per-frame.</summary>
    private static Bitmap RenderTemplatePreview(TemplateDefinition template, double maxDim = 220)
    {
        double scale = Math.Min(maxDim / template.CanvasWidth, maxDim / template.CanvasHeight);
        int pixelW = Math.Max(1, (int)Math.Round(template.CanvasWidth * scale));
        int pixelH = Math.Max(1, (int)Math.Round(template.CanvasHeight * scale));

        var rtb = new RenderTargetBitmap(new PixelSize(pixelW, pixelH));
        using (var dc = rtb.CreateDrawingContext())
        {
            using (dc.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                EditorCanvasControl.RenderElementsStatic(dc, template.BuildElements(), template.BackgroundColor,
                    template.CanvasWidth, template.CanvasHeight);
            }
        }
        return rtb;
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private void RefreshCanvas()
    {
        if (CanvasControl == null) return;
        CanvasControl.SetElements(Elements.ToList());
        CanvasControl.SetBackgroundColor(BackgroundColor);
        // Selection is pushed to the canvas explicitly (OnSelectedElementChanged / multi-select
        // operations) — never reasserted here, or a live multi-selection would get stomped
        // back down to a single element on every unrelated mutation.
    }

    private RenderTargetBitmap? RenderCanvasToBitmap()
    {
        if (CanvasControl == null) return null;
        var pixelSize = new PixelSize((int)_canvasWidth, (int)_canvasHeight);
        var rtb = new RenderTargetBitmap(pixelSize);
        using var dc = rtb.CreateDrawingContext();

        // Background
        dc.DrawRectangle(new SolidColorBrush(BackgroundColor), null,
            new Rect(0, 0, _canvasWidth, _canvasHeight));

        // Elements
        foreach (var el in Elements.OrderBy(e => e.ZIndex))
        {
            el.Draw(dc);
        }

        return rtb;
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    // ── Partial property changed handlers ─────────────────────────────────
    partial void OnSelectedElementChanged(CanvasElement? value)
    {
        if (_isSyncingSelectionFromCanvas) return;

        CanvasControl?.SetSelected(value);
        SelectedElements.Clear();
        if (value != null) SelectedElements.Add(value);
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(HasTripleSelection));
        OnPropertyChanged(nameof(SelectedImageElement));
        OnPropertyChanged(nameof(CanUngroupSelection));
        OnPropertyChanged(nameof(SelectedFillable));
        OnPropertyChanged(nameof(SelectedStrokeable));
        OnPropertyChanged(nameof(SelectedTextElement));
        OnPropertyChanged(nameof(SelectedRectangleElement));
        OnPropertyChanged(nameof(IsTextSelected));
        OnPropertyChanged(nameof(IsShapeSelected));
        OnPropertyChanged(nameof(IsImageSelected));
        OnPropertyChanged(nameof(HasSelection));
        if (value is not ImageElement && ActiveInspectorTab == "adjust")
        {
            ActiveInspectorTab = "properties";
        }
        SyncAdjustmentSlidersToSelection();
        SyncStyleFieldsToSelection();
    }

    partial void OnBackgroundColorChanged(Color value)
    {
        CanvasControl?.SetBackgroundColor(value);
        CanvasControl?.Refresh();
    }

    // ── IDisposable ────────────────────────────────────────────────────────
    public void Dispose()
    {
        // Stop pending debounce timers first — a tick firing after teardown would touch
        // elements/bitmaps this method is about to dispose or clear.
        CancelPendingAdjustmentDebounce();
        CancelPendingStyleDebounce();

        // Dispose any loaded image bitmaps (both the raw source and any baked adjustment).
        foreach (var el in Elements.OfType<ImageElement>())
        {
            el.AdjustmentVersion++; // invalidate any in-flight RecomputeImageAdjustmentsAsync
            el.Source?.Dispose();
            el.AdjustedSource?.Dispose();
        }
        Elements.Clear();

        foreach (var card in TemplateCards) card.Preview?.Dispose();
        TemplateCards.Clear();
        FilteredTemplateCards.Clear();
    }
}

/// <summary>Full undo/redo-relevant state of an ImageElement's adjustment pipeline —
/// replaces an ever-widening anonymous tuple as new sliders were added.</summary>
public readonly record struct ImageAdjustmentSnapshot(
    double Brightness, double Contrast, double Saturation,
    double Hue, double Temperature, double Tint,
    string? Preset, double BlurRadius, double SharpenAmount);

/// <summary>Display wrapper for one Template Gallery card — pairs the template definition with
/// its once-rendered preview bitmap.</summary>
public sealed class TemplateCardViewModel
{
    public TemplateDefinition Definition { get; }
    public Bitmap? Preview { get; }
    public string Name => Definition.Name;
    public string Category => Definition.Category;
    public string DimensionsLabel => $"{(int)Definition.CanvasWidth} × {(int)Definition.CanvasHeight}";

    public TemplateCardViewModel(TemplateDefinition definition, Bitmap? preview)
    {
        Definition = definition;
        Preview = preview;
    }
}
