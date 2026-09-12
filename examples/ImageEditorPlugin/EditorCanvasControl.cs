using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PdfEditorApp.Plugins.ImageEditor.Models;

namespace PdfEditorApp.Plugins.ImageEditor;

/// <summary>Snapshot of an ImageElement's crop/frame state, used to undo/redo a crop.</summary>
public readonly record struct ImageCropState(Rect? SourceCropRect, double X, double Y, double Width, double Height);

/// <summary>
/// Custom Avalonia Control that renders the canvas elements through a zoom/pan viewport
/// and handles pointer interaction for selecting, moving, resizing, placing new shapes,
/// and cropping images.
/// </summary>
public class EditorCanvasControl : Control
{
    // ── Handle geometry (screen-space; divided by zoom wherever compared against
    //    canvas-space coordinates, so handles read as a constant screen size) ──────
    private const double HandleSize = 8;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;

    // ── State ──────────────────────────────────────────────────────────────
    private List<CanvasElement> _elements = new();
    private readonly List<CanvasElement> _selection = new();
    private Color _backgroundColor = Colors.White;

    // Drag / resize state (all in canvas-space, i.e. post ScreenToCanvas)
    private bool _isDragging;
    private bool _isResizing;
    private bool _dragOccurred;
    private int _resizeHandle = -1; // 0-7 → TL, T, TR, R, BR, B, BL, L
    private Point _dragStart;
    private double _origX, _origY, _origW, _origH;
    private Dictionary<CanvasElement, Point>? _groupDragOrigins;
    private CanvasElement? _pressedElement;

    // Marquee selection
    private bool _isMarqueeSelecting;
    private Point _marqueeStart, _marqueeEnd;

    // Tool-placement state machine
    private bool _isPlacingShape;
    private Point _shapeDragStart, _shapeDragEnd;

    // Crop mode
    private bool _isCropModeActive;
    private Rect _cropPreviewRect;
    private bool _isCropDragging;
    private int _cropResizeHandle = -1;
    private double _cropOrigX, _cropOrigY, _cropOrigW, _cropOrigH;
    private double? _cropLockedAspectRatio;

    // Free-angle rotation (single or multi-selection, pivot = own center for N=1)
    private bool _isRotating;
    private Point _rotationPivot;
    private double _rotationStartAngle;
    private Dictionary<CanvasElement, (double X, double Y, double Rotation)>? _rotationOrigins;

    // Proportional group-resize (2+ selected)
    private bool _isGroupResizing;
    private int _groupResizeHandle = -1;
    private Rect _groupResizeOrigBounds;
    private Dictionary<CanvasElement, Rect>? _groupResizeOrigins;

    // Smart alignment guides (rendered only while dragging)
    private List<(bool IsVertical, double Position)> _activeGuides = new();

    // Arrow-key nudge (committed as one undo step after a short idle window)
    private Dictionary<CanvasElement, Point>? _nudgeOrigins;
    private readonly DispatcherTimer _nudgeCommitTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    // Zoom / pan viewport state
    private double _logicalWidth = 640, _logicalHeight = 480;
    private double _zoom = 1.0;
    private Point _panOffset;
    private bool _isFitModeActive = true;

    // Panning (screen-space)
    private bool _isPanning;
    private bool _spaceHeld;
    private Point _panLastScreenPos;

    static EditorCanvasControl()
    {
        FocusableProperty.OverrideDefaultValue<EditorCanvasControl>(true);
    }

    public EditorCanvasControl()
    {
        _nudgeCommitTimer.Tick += OnNudgeCommitTick;
    }

    // ── Public events ──────────────────────────────────────────────────────
    public event Action<IReadOnlyList<CanvasElement>>? SelectionChanged;
    public event Action? CanvasChanged;
    public event Action<double>? ZoomChanged;
    public event Action<IReadOnlyList<(CanvasElement Element, Point OldPos, Point NewPos)>>? ElementsMoved;
    public event Action<CanvasElement, Rect, Rect>? ElementResized;

    /// <summary>Fired when the Text tool is active and the user clicks the canvas.</summary>
    public event Action<Point>? PlaceTextRequested;

    /// <summary>Fired when a Rect/Ellipse/Arrow tool placement completes (click or drag-to-size).</summary>
    public event Action<string, Point, Point>? ShapePlaced;

    /// <summary>Fired after a crop is confirmed or reset, carrying before/after state for undo.</summary>
    public event Action<ImageElement, ImageCropState, ImageCropState>? ElementCropped;

    /// <summary>Fired once the Eyedropper tool samples a pixel.</summary>
    public event Action<Color>? ColorSampled;

    public double CanvasOriginScreenX => (Bounds.Width - _logicalWidth * _zoom) / 2 + _panOffset.X;
    public double CanvasOriginScreenY => (Bounds.Height - _logicalHeight * _zoom) / 2 + _panOffset.Y;
    public event Action<Point>? CursorPositionChanged;

    private bool _isGridVisible = true;
    public bool IsGridVisible
    {
        get => _isGridVisible;
        set
        {
            _isGridVisible = value;
            InvalidateVisual();
        }
    }

    /// <summary>Fired after a single-element (or N=1 group) rotation-handle drag completes.</summary>
    public event Action<CanvasElement, (double X, double Y, double Rotation), (double X, double Y, double Rotation)>? ElementRotated;

    /// <summary>Fired after a multi-element rotation-handle drag completes.</summary>
    public event Action<IReadOnlyDictionary<CanvasElement, (double X, double Y, double Rotation)>, IReadOnlyDictionary<CanvasElement, (double X, double Y, double Rotation)>>? GroupRotated;

    /// <summary>Fired after a proportional group-resize drag completes.</summary>
    public event Action<IReadOnlyList<(CanvasElement Element, Rect Old, Rect New)>>? GroupResized;

    /// <summary>Fired when the user double-clicks a TextElement while the Select tool is active.</summary>
    public event Action<TextElement>? TextEditRequested;

    // ── Tool mode ──────────────────────────────────────────────────────────
    public string ActiveToolMode { get; set; } = "select";

    // ── Multi-select ───────────────────────────────────────────────────────
    public IReadOnlyList<CanvasElement> Selection => _selection;

    public void SetSelection(IEnumerable<CanvasElement> elements)
    {
        _selection.Clear();
        _selection.AddRange(elements);
        InvalidateVisual();
    }

    /// <summary>Convenience for single-element selection (also used by the crop mode's
    /// "only one image selected" precondition).</summary>
    public void SetSelected(CanvasElement? element)
    {
        _selection.Clear();
        if (element != null) _selection.Add(element);
        InvalidateVisual();
    }

    // ── Crop mode ──────────────────────────────────────────────────────────
    public bool IsCropModeActive
    {
        get => _isCropModeActive;
        set
        {
            _isCropModeActive = value;
            if (value) InitializeCropRectForSelection();
            InvalidateVisual();
        }
    }

    private void InitializeCropRectForSelection()
    {
        _cropLockedAspectRatio = null;
        if (_selection.Count == 1 && _selection[0] is ImageElement img)
        {
            _cropPreviewRect = img.Bounds;
        }
        else
        {
            _isCropModeActive = false;
        }
    }

    /// <summary>Snaps the pending crop rect to a fixed aspect ratio, centered within the
    /// image's current frame, and locks that ratio for subsequent handle drags. Pass null for
    /// "Original" (the source image's natural aspect ratio).</summary>
    public void SetCropAspectPreset(double? ratio)
    {
        if (!_isCropModeActive || _selection.Count != 1 || _selection[0] is not ImageElement img) return;

        var frame = img.Bounds;
        double r = ratio ?? (img.NaturalPixelWidth > 0 && img.NaturalPixelHeight > 0
            ? (double)img.NaturalPixelWidth / img.NaturalPixelHeight
            : frame.Width / Math.Max(frame.Height, 0.01));
        if (r <= 0) return;

        double w = frame.Width, h = frame.Width / r;
        if (h > frame.Height) { h = frame.Height; w = frame.Height * r; }

        _cropLockedAspectRatio = r;
        _cropPreviewRect = new Rect(frame.X + (frame.Width - w) / 2, frame.Y + (frame.Height - h) / 2, w, h);
        InvalidateVisual();
    }

    public void ConfirmCrop()
    {
        if (_selection.Count != 1 || _selection[0] is not ImageElement img)
        {
            IsCropModeActive = false;
            return;
        }

        var before = new ImageCropState(img.SourceCropRect, img.X, img.Y, img.Width, img.Height);

        var srcRect = img.EffectiveSourceRect;
        double scaleX = img.Width > 0 ? srcRect.Width / img.Width : 1;
        double scaleY = img.Height > 0 ? srcRect.Height / img.Height : 1;
        double localDx = _cropPreviewRect.X - img.X;
        double localDy = _cropPreviewRect.Y - img.Y;

        img.SourceCropRect = new Rect(
            srcRect.X + localDx * scaleX, srcRect.Y + localDy * scaleY,
            _cropPreviewRect.Width * scaleX, _cropPreviewRect.Height * scaleY);
        img.X = _cropPreviewRect.X;
        img.Y = _cropPreviewRect.Y;
        img.Width = _cropPreviewRect.Width;
        img.Height = _cropPreviewRect.Height;

        var after = new ImageCropState(img.SourceCropRect, img.X, img.Y, img.Width, img.Height);

        _isCropModeActive = false;
        _cropLockedAspectRatio = null;
        CanvasChanged?.Invoke();
        ElementCropped?.Invoke(img, before, after);
        InvalidateVisual();
    }

    public void CancelCrop()
    {
        _isCropModeActive = false;
        _cropLockedAspectRatio = null;
        InvalidateVisual();
    }

    public void ResetCrop()
    {
        if (_selection.Count != 1 || _selection[0] is not ImageElement img) return;

        var before = new ImageCropState(img.SourceCropRect, img.X, img.Y, img.Width, img.Height);
        img.SourceCropRect = null;
        var after = new ImageCropState(img.SourceCropRect, img.X, img.Y, img.Width, img.Height);

        _cropLockedAspectRatio = null;
        CanvasChanged?.Invoke();
        ElementCropped?.Invoke(img, before, after);
        InvalidateVisual();
    }

    // ── Public API ─────────────────────────────────────────────────────────
    public void SetElements(List<CanvasElement> elements)
    {
        // Sorted once here (called after every add/remove/Z-order/undo-redo mutation via
        // RefreshCanvas/Elements.CollectionChanged) rather than on every single Render() call,
        // which used to re-run a LINQ sort on every pointer-move tick during a drag.
        _elements = elements.OrderBy(e => e.ZIndex).ToList();
        InvalidateVisual();
    }

    public void SetBackgroundColor(Color color)
    {
        _backgroundColor = color;
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    /// <summary>The fixed logical canvas size (independent of the control's rendered
    /// bounds) — this is what PNG export/crop/etc. operate against.</summary>
    public void SetLogicalCanvasSize(double width, double height)
    {
        _logicalWidth = width;
        _logicalHeight = height;
        if (_isFitModeActive) FitToWindow();
        InvalidateVisual();
    }

    public double Zoom => _zoom;

    public void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(Math.Round(zoom, 3), MinZoom, MaxZoom);
        _isFitModeActive = false;
        ZoomChanged?.Invoke(_zoom);
        InvalidateVisual();
    }

    public void ZoomBy(double factor) => SetZoom(_zoom * factor);

    public void ResetZoomToActual()
    {
        _panOffset = default;
        SetZoom(1.0);
    }

    public void FitToWindow()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || _logicalWidth <= 0 || _logicalHeight <= 0) return;

        const double padding = 24;
        double availableW = Math.Max(Bounds.Width - padding * 2, 10);
        double availableH = Math.Max(Bounds.Height - padding * 2, 10);
        double fitZoom = Math.Min(availableW / _logicalWidth, availableH / _logicalHeight);

        _zoom = Math.Clamp(Math.Round(fitZoom, 3), MinZoom, MaxZoom);
        _panOffset = default;
        _isFitModeActive = true;
        ZoomChanged?.Invoke(_zoom);
        InvalidateVisual();
    }

    // ── Viewport transform ─────────────────────────────────────────────────
    private Matrix GetViewportMatrix()
    {
        double offsetX = (Bounds.Width - _logicalWidth * _zoom) / 2 + _panOffset.X;
        double offsetY = (Bounds.Height - _logicalHeight * _zoom) / 2 + _panOffset.Y;
        return new Matrix(_zoom, 0, 0, _zoom, offsetX, offsetY);
    }

    private Point ScreenToCanvas(Point screenPt)
    {
        var m = GetViewportMatrix();
        return m.TryInvert(out var inv) ? screenPt.Transform(inv) : screenPt;
    }

    private void ClampPanOffset()
    {
        double renderedW = _logicalWidth * _zoom;
        double renderedH = _logicalHeight * _zoom;
        const double minVisible = 40;

        double centerOffsetX = (Bounds.Width - renderedW) / 2;
        double centerOffsetY = (Bounds.Height - renderedH) / 2;

        double minPanX = -renderedW - centerOffsetX + minVisible;
        double maxPanX = Bounds.Width - centerOffsetX - minVisible;
        double minPanY = -renderedH - centerOffsetY + minVisible;
        double maxPanY = Bounds.Height - centerOffsetY - minVisible;

        double x = minPanX <= maxPanX ? Math.Clamp(_panOffset.X, minPanX, maxPanX) : _panOffset.X;
        double y = minPanY <= maxPanY ? Math.Clamp(_panOffset.Y, minPanY, maxPanY) : _panOffset.Y;
        _panOffset = new Point(x, y);
    }

    // ── Render ─────────────────────────────────────────────────────────────
    public override void Render(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);

        // Pasteboard behind the logical canvas (visible when zoomed/panned out of frame).
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)), null, bounds);

        using (dc.PushTransform(GetViewportMatrix()))
        {
            var canvasRect = new Rect(0, 0, _logicalWidth, _logicalHeight);

            // Physical artboard drop shadow and depth (gives realistic paper elevation on pasteboard)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), null, new Rect(-4, 4, _logicalWidth + 8, _logicalHeight + 10));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, new Rect(-1, 1, _logicalWidth + 2, _logicalHeight + 3));

            // 1. Canvas background — checkerboard is cached to a tile bitmap (regenerated
            // only when the logical canvas size changes) rather than redrawn cell-by-cell on
            // every Render() call, which used to run during every drag/pan/zoom tick.
            if (_backgroundColor.A == 0)
            {
                dc.DrawImage(GetCheckerboardTile(_logicalWidth, _logicalHeight), canvasRect);
            }
            else
            {
                dc.DrawRectangle(new SolidColorBrush(_backgroundColor), null, canvasRect);
            }

            // 2. Canvas grid dots (subtle) — same tile-caching treatment (~7000 DrawEllipse
            // calls on a large canvas, otherwise re-run on every single-pointer-move tick).
            if (_isGridVisible)
            {
                dc.DrawImage(GetGridTile(_logicalWidth, _logicalHeight), canvasRect);
            }

            // Artboard crisp hairline boundary
            var outlineColor = (_backgroundColor.A == 0 || (_backgroundColor.R > 200 && _backgroundColor.G > 200 && _backgroundColor.B > 200))
                ? Color.FromArgb(45, 0, 0, 0)
                : Color.FromArgb(55, 255, 255, 255);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(outlineColor), 1), canvasRect);

            // 3. Elements (already Z-sorted by SetElements), each with its own rotate/flip transform
            foreach (var el in _elements)
            {
                DrawElement(dc, el);
            }

            // 4. Selection / marquee / shape-preview / crop overlay
            if (_isCropModeActive && _selection.Count == 1 && _selection[0] is ImageElement cropImg)
            {
                DrawCropOverlay(dc, cropImg, _zoom);
            }
            else if (_selection.Count == 1)
            {
                DrawSelectionHandles(dc, _selection[0], _zoom);
                DrawRotationHandle(dc, _selection[0].Bounds, _selection[0].RotationDegrees, _zoom);
            }
            else if (_selection.Count > 1)
            {
                DrawMultiSelectionOutline(dc, _selection, _zoom);
                var groupBounds = ComputeGroupBounds(_selection);
                DrawResizeHandles(dc, groupBounds, _zoom);
                DrawRotationHandle(dc, groupBounds, 0, _zoom);
            }

            if (_isMarqueeSelecting)
            {
                DrawMarquee(dc, _marqueeStart, _marqueeEnd, _zoom);
            }

            if (_isPlacingShape)
            {
                DrawShapePlacementPreview(dc, ActiveToolMode, _shapeDragStart, _shapeDragEnd, _zoom);
            }

            if (_activeGuides.Count > 0)
            {
                DrawAlignmentGuides(dc, _activeGuides, _zoom);
            }
        }
    }

    /// <summary>Renders a flat, already-Z-ordered element list (background + elements) into an
    /// arbitrary DrawingContext — shared by the live canvas Render() and standalone preview
    /// rendering (the template gallery), so both paths produce identical visual output.</summary>
    public static void RenderElementsStatic(DrawingContext dc, IEnumerable<CanvasElement> elements, Color background, double width, double height)
    {
        var canvasRect = new Rect(0, 0, width, height);
        if (background.A == 0)
        {
            DrawCheckerboardBackground(dc, canvasRect);
        }
        else
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, canvasRect);
        }
        foreach (var el in elements.OrderBy(e => e.ZIndex))
        {
            DrawElement(dc, el);
        }
    }

    private static void DrawElement(DrawingContext dc, CanvasElement el)
    {
        if (!el.IsVisible) return;

        // Guarded so Opacity==1.0 (the common case) skips pushing an extra Skia layer entirely.
        IDisposable? opacityScope = el.Opacity < 1.0 ? dc.PushOpacity(Math.Clamp(el.Opacity, 0, 1)) : null;
        try
        {
            if (el.RotationDegrees == 0 && !el.FlipX && !el.FlipY)
            {
                el.Draw(dc);
                return;
            }

            var center = el.Bounds.Center;
            double theta = el.RotationDegrees * Math.PI / 180.0;
            double sx = el.FlipX ? -1.0 : 1.0;
            double sy = el.FlipY ? -1.0 : 1.0;

            // Flip about the element's center, then rotate about the same center.
            var m = Matrix.CreateTranslation(-center.X, -center.Y)
                  * Matrix.CreateScale(sx, sy)
                  * Matrix.CreateTranslation(center.X, center.Y)
                  * Matrix.CreateRotation(theta, center);

            using (dc.PushTransform(m))
            {
                el.Draw(dc);
            }
        }
        finally
        {
            opacityScope?.Dispose();
        }
    }

    // ── Background/grid tile caches (regenerated only when the logical canvas size changes,
    // never per-frame) ───────────────────────────────────────────────────────────────────
    private RenderTargetBitmap? _cachedGridTile;
    private double _cachedGridWidth = -1, _cachedGridHeight = -1;
    private RenderTargetBitmap? _cachedCheckerTile;
    private double _cachedCheckerWidth = -1, _cachedCheckerHeight = -1;

    private RenderTargetBitmap GetGridTile(double width, double height)
    {
        if (_cachedGridTile == null || _cachedGridWidth != width || _cachedGridHeight != height)
        {
            _cachedGridTile?.Dispose();
            var pixelSize = new PixelSize(Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, (int)Math.Ceiling(height)));
            _cachedGridTile = new RenderTargetBitmap(pixelSize);
            using (var dc = _cachedGridTile.CreateDrawingContext())
            {
                DrawGrid(dc, new Rect(0, 0, width, height));
            }
            _cachedGridWidth = width;
            _cachedGridHeight = height;
        }
        return _cachedGridTile;
    }

    private RenderTargetBitmap GetCheckerboardTile(double width, double height)
    {
        if (_cachedCheckerTile == null || _cachedCheckerWidth != width || _cachedCheckerHeight != height)
        {
            _cachedCheckerTile?.Dispose();
            var pixelSize = new PixelSize(Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, (int)Math.Ceiling(height)));
            _cachedCheckerTile = new RenderTargetBitmap(pixelSize);
            using (var dc = _cachedCheckerTile.CreateDrawingContext())
            {
                DrawCheckerboardBackground(dc, new Rect(0, 0, width, height));
            }
            _cachedCheckerWidth = width;
            _cachedCheckerHeight = height;
        }
        return _cachedCheckerTile;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _cachedGridTile?.Dispose();
        _cachedGridTile = null;
        _cachedGridWidth = -1;
        _cachedGridHeight = -1;

        _cachedCheckerTile?.Dispose();
        _cachedCheckerTile = null;
        _cachedCheckerWidth = -1;
        _cachedCheckerHeight = -1;

        _nudgeCommitTimer.Stop();
    }

    private static void DrawCheckerboardBackground(DrawingContext dc, Rect bounds)
    {
        const double cell = 16;
        var dark = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x35));
        var light = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x47));
        dc.DrawRectangle(dark, null, bounds);

        int cols = (int)Math.Ceiling(bounds.Width / cell);
        int rows = (int)Math.Ceiling(bounds.Height / cell);
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                if ((row + col) % 2 != 0) continue;
                double x = bounds.X + col * cell;
                double y = bounds.Y + row * cell;
                double w = Math.Min(cell, bounds.Right - x);
                double h = Math.Min(cell, bounds.Bottom - y);
                if (w <= 0 || h <= 0) continue;
                dc.DrawRectangle(light, null, new Rect(x, y, w, h));
            }
        }
    }

    private static void DrawGrid(DrawingContext dc, Rect bounds)
    {
        var dotBrush = new SolidColorBrush(Color.FromArgb(30, 150, 150, 150));
        const double spacing = 20;
        for (double x = spacing; x < bounds.Width; x += spacing)
        {
            for (double y = spacing; y < bounds.Height; y += spacing)
            {
                dc.DrawEllipse(dotBrush, null, new Point(x, y), 1, 1);
            }
        }
    }

    private static void DrawSelectionHandles(DrawingContext dc, CanvasElement el, double zoom)
    {
        var bounds = el.Bounds;

        // Dashed selection border
        var borderPen = new Pen(
            new SolidColorBrush(Color.FromArgb(220, 99, 102, 241)),
            1.5 / zoom,
            new DashStyle(new double[] { 5, 3 }, 0));
        dc.DrawRectangle(null, borderPen, bounds.Inflate(2 / zoom));

        DrawResizeHandles(dc, bounds, zoom);
    }

    /// <summary>8 resize handles around a bounds rect — shared by single-element selection
    /// and (with a group's bounding box) group-resize.</summary>
    private static void DrawResizeHandles(DrawingContext dc, Rect bounds, double zoom)
    {
        double handleSize = HandleSize / zoom;
        double halfHandle = handleSize / 2;

        var handleFill = new SolidColorBrush(Colors.White);
        var handlePen = new Pen(new SolidColorBrush(Color.FromArgb(255, 99, 102, 241)), 1.5 / zoom);

        foreach (var pt in GetHandlePoints(bounds))
        {
            dc.DrawRectangle(handleFill, handlePen,
                new Rect(pt.X - halfHandle, pt.Y - halfHandle, handleSize, handleSize),
                2 / zoom, 2 / zoom);
        }
    }

    private static void DrawMultiSelectionOutline(DrawingContext dc, IReadOnlyList<CanvasElement> selection, double zoom)
    {
        var memberPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 99, 102, 241)), 1 / zoom,
            new DashStyle(new double[] { 4, 3 }, 0));
        foreach (var el in selection)
        {
            dc.DrawRectangle(null, memberPen, el.GetRotatedBounds().Inflate(1 / zoom));
        }

        var groupPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 99, 102, 241)), 1.5 / zoom,
            new DashStyle(new double[] { 6, 3 }, 0));
        dc.DrawRectangle(null, groupPen, ComputeGroupBounds(selection).Inflate(4 / zoom));
    }

    /// <summary>Rotation handle sits a fixed screen-space distance above the (rotated, for a
    /// single element) top-center point. rotationDegrees is 0 for a group handle, since the
    /// synthetic group bounding box never itself carries a rotation.</summary>
    private static void DrawRotationHandle(DrawingContext dc, Rect axisBounds, double rotationDegrees, double zoom)
    {
        var stemStart = GetRotationHandleStemStart(axisBounds, rotationDegrees);
        var handlePt = GetRotationHandlePoint(axisBounds, rotationDegrees, zoom);
        var stemPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 99, 102, 241)), 1.5 / zoom);
        dc.DrawLine(stemPen, stemStart, handlePt);
        double r = 5.5 / zoom;
        dc.DrawEllipse(new SolidColorBrush(Colors.White), stemPen, handlePt, r, r);
    }

    private static Point GetRotationHandleStemStart(Rect axisBounds, double rotationDegrees)
    {
        var localTop = new Point(axisBounds.X + axisBounds.Width / 2, axisBounds.Y);
        if (rotationDegrees == 0) return localTop;
        return localTop.Transform(Matrix.CreateRotation(rotationDegrees * Math.PI / 180.0, axisBounds.Center));
    }

    private static Point GetRotationHandlePoint(Rect axisBounds, double rotationDegrees, double zoom)
    {
        double offset = 28 / zoom;
        var localHandle = new Point(axisBounds.X + axisBounds.Width / 2, axisBounds.Y - offset);
        if (rotationDegrees == 0) return localHandle;
        return localHandle.Transform(Matrix.CreateRotation(rotationDegrees * Math.PI / 180.0, axisBounds.Center));
    }

    private static void DrawAlignmentGuides(DrawingContext dc, List<(bool IsVertical, double Position)> guides, double zoom)
    {
        const double span = 4000;
        var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 236, 72, 153)), 1 / zoom,
            new DashStyle(new double[] { 4, 3 }, 0));
        foreach (var (isVertical, pos) in guides)
        {
            if (isVertical) dc.DrawLine(guidePen, new Point(pos, -span), new Point(pos, span));
            else dc.DrawLine(guidePen, new Point(-span, pos), new Point(span, pos));
        }
    }

    private static Rect ComputeGroupBounds(IReadOnlyList<CanvasElement> selection)
    {
        var result = selection[0].GetRotatedBounds();
        for (int i = 1; i < selection.Count; i++)
        {
            result = result.Union(selection[i].GetRotatedBounds());
        }
        return result;
    }

    private static void DrawMarquee(DrawingContext dc, Point start, Point end, double zoom)
    {
        var rect = new Rect(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                             Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 99, 102, 241)),
            new Pen(new SolidColorBrush(Color.FromArgb(200, 99, 102, 241)), 1 / zoom), rect);
    }

    private static void DrawShapePlacementPreview(DrawingContext dc, string toolType, Point start, Point end, double zoom)
    {
        var rect = new Rect(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                             Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 99, 102, 241)), 1.5 / zoom,
            new DashStyle(new double[] { 4, 3 }, 0));
        var fill = new SolidColorBrush(Color.FromArgb(40, 99, 102, 241));

        switch (toolType)
        {
            case "ellipse":
                dc.DrawEllipse(fill, pen, rect.Center, rect.Width / 2, rect.Height / 2);
                break;
            case "arrow":
                dc.DrawLine(pen, start, end);
                break;
            default:
                dc.DrawRectangle(fill, pen, rect);
                break;
        }
    }

    private void DrawCropOverlay(DrawingContext dc, ImageElement img, double zoom)
    {
        var frame = img.Bounds;

        // Dark mask over the whole frame, then re-reveal the pending crop region clipped
        // and at full brightness.
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, frame);
        using (dc.PushClip(_cropPreviewRect))
        {
            DrawElement(dc, img);
        }

        double handleSize = HandleSize / zoom;
        double halfHandle = handleSize / 2;
        var accentPen = new Pen(new SolidColorBrush(Color.FromArgb(255, 16, 185, 129)), 1.5 / zoom);
        dc.DrawRectangle(null, accentPen, _cropPreviewRect);

        var handleFill = new SolidColorBrush(Colors.White);
        foreach (var pt in GetHandlePoints(_cropPreviewRect))
        {
            dc.DrawRectangle(handleFill, accentPen,
                new Rect(pt.X - halfHandle, pt.Y - halfHandle, handleSize, handleSize), 2 / zoom, 2 / zoom);
        }
    }

    // TL, T, TR, R, BR, B, BL, L
    private static Point[] GetHandlePoints(Rect r)
    {
        double cx = r.X + r.Width / 2;
        double cy = r.Y + r.Height / 2;
        return
        [
            new Point(r.X,            r.Y),
            new Point(cx,             r.Y),
            new Point(r.Right,        r.Y),
            new Point(r.Right,        cy),
            new Point(r.Right,        r.Bottom),
            new Point(cx,             r.Bottom),
            new Point(r.X,            r.Bottom),
            new Point(r.X,            cy),
        ];
    }

    // ── Pointer Events ─────────────────────────────────────────────────────
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var props = e.GetCurrentPoint(this).Properties;

        // 1. Panning pre-empts every other interaction.
        if (props.IsMiddleButtonPressed || _spaceHeld)
        {
            _isPanning = true;
            _panLastScreenPos = e.GetPosition(this);
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        var canvasPt = ScreenToCanvas(e.GetPosition(this));
        _dragStart = canvasPt;

        // 2. Crop mode is modal.
        if (_isCropModeActive)
        {
            HandleCropPointerPressed(canvasPt);
            e.Handled = true;
            return;
        }

        // 3. Tool-placement mode.
        if (ActiveToolMode != "select")
        {
            if (ActiveToolMode == "text")
            {
                PlaceTextRequested?.Invoke(canvasPt);
            }
            else if (ActiveToolMode is "rect" or "ellipse" or "arrow")
            {
                _isPlacingShape = true;
                _shapeDragStart = canvasPt;
                _shapeDragEnd = canvasPt;
            }
            else if (ActiveToolMode == "eyedropper")
            {
                var sampled = SampleColorAtCanvasPoint(canvasPt);
                if (sampled is { } color) ColorSampled?.Invoke(color);
            }
            e.Handled = true;
            return;
        }

        bool shiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _dragOccurred = false;

        // 4. Resize handle of the current single-element selection.
        if (_selection.Count == 1 && !_selection[0].IsLocked)
        {
            var single = _selection[0];
            double handleSize = HandleSize / _zoom;
            double halfHandle = handleSize / 2;
            double pad = 4 / _zoom;
            var handles = GetHandlePoints(single.Bounds);
            for (int i = 0; i < handles.Length; i++)
            {
                var hr = new Rect(handles[i].X - halfHandle - pad, handles[i].Y - halfHandle - pad,
                                  handleSize + pad * 2, handleSize + pad * 2);
                if (hr.Contains(canvasPt))
                {
                    _isResizing = true;
                    _resizeHandle = i;
                    _origX = single.X;
                    _origY = single.Y;
                    _origW = single.Width;
                    _origH = single.Height;
                    // Manual resize takes precedence over auto-fit-to-text from now on,
                    // otherwise the very next Draw() would snap the size back. Only the axes
                    // actually affected by this handle stop auto-fitting (a pure T/B drag
                    // shouldn't block width from still auto-fitting, and vice versa).
                    if (single is TextElement textEl)
                    {
                        // Handle indices: 0=TL 1=T 2=TR 3=R 4=BR 5=B 6=BL 7=L
                        bool affectsWidth = i is not (1 or 5);  // all except pure T/B
                        bool affectsHeight = i is not (3 or 7); // all except pure L/R
                        if (affectsWidth) textEl.IsWidthAutoFitEnabled = false;
                        if (affectsHeight) textEl.IsHeightAutoFitEnabled = false;
                    }
                    e.Handled = true;
                    return;
                }
            }
        }

        // 5. Group-resize handle (2+ selected, all unlocked).
        if (_selection.Count > 1 && _selection.All(s => !s.IsLocked))
        {
            var groupBounds = ComputeGroupBounds(_selection);
            double handleSize = HandleSize / _zoom;
            double halfHandle = handleSize / 2;
            double pad = 4 / _zoom;
            var handles = GetHandlePoints(groupBounds);
            for (int i = 0; i < handles.Length; i++)
            {
                var hr = new Rect(handles[i].X - halfHandle - pad, handles[i].Y - halfHandle - pad,
                                  handleSize + pad * 2, handleSize + pad * 2);
                if (hr.Contains(canvasPt))
                {
                    _isGroupResizing = true;
                    _groupResizeHandle = i;
                    _groupResizeOrigBounds = groupBounds;
                    _groupResizeOrigins = _selection.ToDictionary(el => el, el => new Rect(el.X, el.Y, el.Width, el.Height));
                    e.Handled = true;
                    return;
                }
            }
        }

        // 6. Rotation handle (single or multi selection, all unlocked).
        if (_selection.Count >= 1 && _selection.All(s => !s.IsLocked))
        {
            Rect axisBounds; double currentRotation;
            if (_selection.Count == 1) { axisBounds = _selection[0].Bounds; currentRotation = _selection[0].RotationDegrees; }
            else { axisBounds = ComputeGroupBounds(_selection); currentRotation = 0; }

            var handlePt = GetRotationHandlePoint(axisBounds, currentRotation, _zoom);
            double hitRadius = 10 / _zoom;
            if (Point.Distance(handlePt, canvasPt) <= hitRadius)
            {
                _isRotating = true;
                _rotationPivot = axisBounds.Center;
                _rotationStartAngle = Math.Atan2(canvasPt.Y - _rotationPivot.Y, canvasPt.X - _rotationPivot.X);
                _rotationOrigins = _selection.ToDictionary(el => el, el => (el.X, el.Y, el.RotationDegrees));
                e.Handled = true;
                return;
            }
        }

        // 7. Double-click on a TextElement while the Select tool is active → inline edit.
        if (e.ClickCount >= 2)
        {
            var doubleClickHit = _elements.OrderByDescending(el => el.ZIndex)
                .FirstOrDefault(el => el.IsVisible && !el.IsLocked && el.HitTestRotationAware(canvasPt));
            if (doubleClickHit is TextElement textHit)
            {
                _selection.Clear();
                _selection.Add(textHit);
                NotifySelectionChanged();
                TextEditRequested?.Invoke(textHit);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        // 8. Hit-test elements (top-most Z first), skipping hidden/locked elements.
        var hit = _elements.OrderByDescending(el => el.ZIndex)
            .FirstOrDefault(el => el.IsVisible && !el.IsLocked && el.HitTestRotationAware(canvasPt));

        if (hit != null)
        {
            if (shiftHeld)
            {
                if (_selection.Contains(hit)) _selection.Remove(hit);
                else _selection.Add(hit);
            }
            else if (!_selection.Contains(hit))
            {
                _selection.Clear();
                _selection.Add(hit);
            }
            // else: hit is already part of a multi-selection and shift isn't held — keep the
            // whole selection intact so drag-to-move-group works; narrowed on release only
            // if no actual drag occurs (see OnPointerReleased).

            NotifySelectionChanged();

            if (_selection.Count > 0)
            {
                _isDragging = true;
                _pressedElement = hit;
                _groupDragOrigins = _selection.ToDictionary(el => el, el => new Point(el.X, el.Y));
            }
        }
        else
        {
            // Empty-canvas click: begin marquee selection.
            if (!shiftHeld) _selection.Clear();
            _isMarqueeSelecting = true;
            _marqueeStart = canvasPt;
            _marqueeEnd = canvasPt;
            NotifySelectionChanged();
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            CanvasChanged?.Invoke();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CursorPositionChanged?.Invoke(new Point(-1, -1));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isPanning)
        {
            var currentScreenPos = e.GetPosition(this);
            var delta = currentScreenPos - _panLastScreenPos;
            _panOffset = new Point(_panOffset.X + delta.X, _panOffset.Y + delta.Y);
            _panLastScreenPos = currentScreenPos;
            ClampPanOffset(); // same unbounded-pan gap as the wheel handler — see its comment
            CursorPositionChanged?.Invoke(ScreenToCanvas(currentScreenPos));
            CanvasChanged?.Invoke();
            InvalidateVisual();
            return;
        }

        var pos = ScreenToCanvas(e.GetPosition(this));
        CursorPositionChanged?.Invoke(pos);

        if (_isCropDragging)
        {
            HandleCropPointerMoved(pos);
            InvalidateVisual();
            return;
        }

        if (_isPlacingShape)
        {
            _shapeDragEnd = pos;
            InvalidateVisual();
            return;
        }

        if (_isMarqueeSelecting)
        {
            _marqueeEnd = pos;
            InvalidateVisual();
            return;
        }

        double dx = pos.X - _dragStart.X;
        double dy = pos.Y - _dragStart.Y;
        if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5) _dragOccurred = true;

        if (_isDragging && _groupDragOrigins != null)
        {
            // Shift constrains the move to a single axis (whichever has the larger delta).
            double moveDx = dx, moveDy = dy;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                if (Math.Abs(dx) >= Math.Abs(dy)) moveDy = 0; else moveDx = 0;
            }

            var (snapDx, snapDy, guides) = ComputeSnapping(moveDx, moveDy);
            _activeGuides = guides;

            foreach (var (el, origin) in _groupDragOrigins)
            {
                el.X = origin.X + snapDx;
                el.Y = origin.Y + snapDy;
            }
            CanvasChanged?.Invoke();
            InvalidateVisual();
        }
        else if (_isResizing && _selection.Count == 1)
        {
            ApplyResize(dx, dy, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            CanvasChanged?.Invoke();
            InvalidateVisual();
        }
        else if (_isGroupResizing && _groupResizeOrigins != null)
        {
            ApplyGroupResize(dx, dy, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            CanvasChanged?.Invoke();
            InvalidateVisual();
        }
        else if (_isRotating && _rotationOrigins != null)
        {
            double currentAngle = Math.Atan2(pos.Y - _rotationPivot.Y, pos.X - _rotationPivot.X);
            double deltaDegrees = (currentAngle - _rotationStartAngle) * 180.0 / Math.PI;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) deltaDegrees = Math.Round(deltaDegrees / 15.0) * 15.0;
            ApplyRotationDrag(deltaDegrees);
            CanvasChanged?.Invoke();
            InvalidateVisual();
        }
    }

    /// <summary>Snaps a candidate move delta so the dragged selection's edges/center align with
    /// other elements' edges/centers or the canvas edges/center, within a small zoom-compensated
    /// tolerance. Returns the (possibly adjusted) delta plus the guide lines to render.</summary>
    private (double Dx, double Dy, List<(bool IsVertical, double Position)> Guides) ComputeSnapping(double dx, double dy)
    {
        var guides = new List<(bool, double)>();
        if (_groupDragOrigins == null || _groupDragOrigins.Count == 0) return (dx, dy, guides);

        Rect? draggedBounds = null;
        foreach (var (el, origin) in _groupDragOrigins)
        {
            var candidate = new Rect(origin.X + dx, origin.Y + dy, el.Width, el.Height);
            draggedBounds = draggedBounds is { } db ? db.Union(candidate) : candidate;
        }
        if (draggedBounds is not { } bounds) return (dx, dy, guides);

        double tolerance = 6 / _zoom;
        var draggedSet = new HashSet<CanvasElement>(_groupDragOrigins.Keys);

        var targetsX = new List<double> { 0, _logicalWidth / 2, _logicalWidth };
        var targetsY = new List<double> { 0, _logicalHeight / 2, _logicalHeight };
        foreach (var other in _elements)
        {
            if (draggedSet.Contains(other) || !other.IsVisible) continue;
            var ob = other.GetRotatedBounds();
            targetsX.Add(ob.X); targetsX.Add(ob.X + ob.Width / 2); targetsX.Add(ob.Right);
            targetsY.Add(ob.Y); targetsY.Add(ob.Y + ob.Height / 2); targetsY.Add(ob.Bottom);
        }

        double snapDx = dx, snapDy = dy;
        var xCandidates = new[] { bounds.X, bounds.X + bounds.Width / 2, bounds.Right };
        var yCandidates = new[] { bounds.Y, bounds.Y + bounds.Height / 2, bounds.Bottom };

        foreach (var xc in xCandidates)
        {
            var match = targetsX.FirstOrDefault(tx => Math.Abs(xc - tx) <= tolerance, double.NaN);
            if (!double.IsNaN(match))
            {
                snapDx += match - xc;
                guides.Add((true, match));
                break;
            }
        }
        foreach (var yc in yCandidates)
        {
            var match = targetsY.FirstOrDefault(ty => Math.Abs(yc - ty) <= tolerance, double.NaN);
            if (!double.IsNaN(match))
            {
                snapDy += match - yc;
                guides.Add((false, match));
                break;
            }
        }

        return (snapDx, snapDy, guides);
    }

    /// <summary>Proportionally resizes every selected element together — each element's new
    /// position/size is derived from its fractional offset within the old vs. new group
    /// bounding box, so the same formula works anchor-agnostically for all 8 handles.</summary>
    private void ApplyGroupResize(double dx, double dy, bool shiftHeld)
    {
        if (_groupResizeOrigins == null) return;

        // Judgment call: the group-level Shift-held lock overrides any individual element's
        // own AspectLocked — only a uniform group-ratio lock applies during group-resize.
        double? lockedRatio = shiftHeld && _groupResizeOrigBounds.Height > 0
            ? _groupResizeOrigBounds.Width / _groupResizeOrigBounds.Height
            : null;

        var newBounds = ComputeResizedRect(_groupResizeOrigBounds, _groupResizeHandle, dx, dy, 20, lockedRatio);
        double sx = _groupResizeOrigBounds.Width > 0 ? newBounds.Width / _groupResizeOrigBounds.Width : 1;
        double sy = _groupResizeOrigBounds.Height > 0 ? newBounds.Height / _groupResizeOrigBounds.Height : 1;

        foreach (var (el, origRect) in _groupResizeOrigins)
        {
            double fracX = _groupResizeOrigBounds.Width > 0 ? (origRect.X - _groupResizeOrigBounds.X) / _groupResizeOrigBounds.Width : 0;
            double fracY = _groupResizeOrigBounds.Height > 0 ? (origRect.Y - _groupResizeOrigBounds.Y) / _groupResizeOrigBounds.Height : 0;
            el.X = newBounds.X + fracX * newBounds.Width;
            el.Y = newBounds.Y + fracY * newBounds.Height;
            el.Width = Math.Max(1, origRect.Width * sx);
            el.Height = Math.Max(1, origRect.Height * sy);
        }
    }

    /// <summary>Rotates every selected element's position around the shared pivot by
    /// deltaDegrees, and adds deltaDegrees to each element's own RotationDegrees. For a single
    /// selection the pivot equals that element's own center, so its position is unchanged and
    /// only RotationDegrees moves — the N=1 special case of the same formula.</summary>
    private void ApplyRotationDrag(double deltaDegrees)
    {
        if (_rotationOrigins == null) return;
        double rad = deltaDegrees * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);

        foreach (var (el, origin) in _rotationOrigins)
        {
            double cx = origin.X + el.Width / 2;
            double cy = origin.Y + el.Height / 2;
            double relX = cx - _rotationPivot.X;
            double relY = cy - _rotationPivot.Y;
            double newCx = _rotationPivot.X + relX * cos - relY * sin;
            double newCy = _rotationPivot.Y + relX * sin + relY * cos;

            el.X = newCx - el.Width / 2;
            el.Y = newCy - el.Height / 2;
            el.RotationDegrees = ((origin.Rotation + deltaDegrees) % 360 + 360) % 360;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isCropDragging)
        {
            _isCropDragging = false;
            _cropResizeHandle = -1;
        }
        else if (_isPlacingShape)
        {
            double dragDist = Point.Distance(_shapeDragStart, _shapeDragEnd);
            var start = _shapeDragStart;
            var end = dragDist < 4 ? _shapeDragStart : _shapeDragEnd;
            ShapePlaced?.Invoke(ActiveToolMode, start, end);
            _isPlacingShape = false;
        }
        else if (_isMarqueeSelecting)
        {
            FinishMarqueeSelection(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            _isMarqueeSelecting = false;
        }
        else if (_isDragging)
        {
            if (!_dragOccurred && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                && _pressedElement != null && _selection.Count > 1)
            {
                _selection.Clear();
                _selection.Add(_pressedElement);
                NotifySelectionChanged();
            }
            else if (_dragOccurred && _groupDragOrigins != null)
            {
                var moves = new List<(CanvasElement Element, Point OldPos, Point NewPos)>();
                foreach (var (el, oldPos) in _groupDragOrigins)
                {
                    var newPos = new Point(el.X, el.Y);
                    if (oldPos != newPos) moves.Add((el, oldPos, newPos));
                }
                if (moves.Count > 0) ElementsMoved?.Invoke(moves);
            }
        }
        else if (_isResizing && _selection.Count == 1)
        {
            var single = _selection[0];
            var oldBounds = new Rect(_origX, _origY, _origW, _origH);
            var newBounds = single.Bounds;
            if (oldBounds != newBounds) ElementResized?.Invoke(single, oldBounds, newBounds);
        }
        else if (_isGroupResizing && _groupResizeOrigins != null)
        {
            var changes = new List<(CanvasElement, Rect, Rect)>();
            foreach (var (el, oldRect) in _groupResizeOrigins)
            {
                var newRect = new Rect(el.X, el.Y, el.Width, el.Height);
                if (oldRect != newRect) changes.Add((el, oldRect, newRect));
            }
            if (changes.Count > 0) GroupResized?.Invoke(changes);
        }
        else if (_isRotating && _rotationOrigins != null)
        {
            if (_rotationOrigins.Count == 1)
            {
                var (el, origin) = _rotationOrigins.First();
                var after = (el.X, el.Y, el.RotationDegrees);
                if (origin != after) ElementRotated?.Invoke(el, origin, after);
            }
            else if (_rotationOrigins.Count > 1)
            {
                var before = new Dictionary<CanvasElement, (double X, double Y, double Rotation)>(_rotationOrigins);
                var after = _rotationOrigins.Keys.ToDictionary(el => el, el => (el.X, el.Y, el.RotationDegrees));
                GroupRotated?.Invoke(before, after);
            }
        }

        _isDragging = false;
        _isResizing = false;
        _isGroupResizing = false;
        _isRotating = false;
        _isPanning = false;
        _dragOccurred = false;
        _resizeHandle = -1;
        _groupResizeHandle = -1;
        _groupDragOrigins = null;
        _groupResizeOrigins = null;
        _rotationOrigins = null;
        _pressedElement = null;
        _activeGuides.Clear();

        InvalidateVisual();
    }

    private void FinishMarqueeSelection(bool shiftHeld)
    {
        var marqueeRect = new Rect(
            Math.Min(_marqueeStart.X, _marqueeEnd.X), Math.Min(_marqueeStart.Y, _marqueeEnd.Y),
            Math.Abs(_marqueeEnd.X - _marqueeStart.X), Math.Abs(_marqueeEnd.Y - _marqueeStart.Y));

        var hits = _elements.Where(el => el.IsVisible && !el.IsLocked && marqueeRect.Intersects(el.GetRotatedBounds())).ToList();

        if (!shiftHeld) _selection.Clear();
        foreach (var el in hits)
        {
            if (!_selection.Contains(el)) _selection.Add(el);
        }
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged() => SelectionChanged?.Invoke(_selection.ToList());

    private void HandleCropPointerPressed(Point canvasPt)
    {
        if (_selection.Count != 1 || _selection[0] is not ImageElement)
        {
            IsCropModeActive = false;
            return;
        }

        double handleSize = HandleSize / _zoom;
        double halfHandle = handleSize / 2;
        double pad = 4 / _zoom;
        var handles = GetHandlePoints(_cropPreviewRect);
        for (int i = 0; i < handles.Length; i++)
        {
            var hr = new Rect(handles[i].X - halfHandle - pad, handles[i].Y - halfHandle - pad,
                              handleSize + pad * 2, handleSize + pad * 2);
            if (hr.Contains(canvasPt))
            {
                _isCropDragging = true;
                _cropResizeHandle = i;
                _cropOrigX = _cropPreviewRect.X;
                _cropOrigY = _cropPreviewRect.Y;
                _cropOrigW = _cropPreviewRect.Width;
                _cropOrigH = _cropPreviewRect.Height;
                return;
            }
        }
    }

    private void HandleCropPointerMoved(Point canvasPt)
    {
        if (_selection.Count != 1 || _selection[0] is not ImageElement img) return;

        double dx = canvasPt.X - _dragStart.X;
        double dy = canvasPt.Y - _dragStart.Y;
        var frame = img.Bounds;
        const double minSize = 10;

        var orig = new Rect(_cropOrigX, _cropOrigY, _cropOrigW, _cropOrigH);
        var result = ComputeResizedRect(orig, _cropResizeHandle, dx, dy, minSize, _cropLockedAspectRatio);

        // Clamp within the element's current frame — the crop rect can only shrink, never
        // extend beyond what's currently visible ("Reset Crop" is how you get that back).
        double x = Math.Clamp(result.X, frame.X, frame.Right - minSize);
        double y = Math.Clamp(result.Y, frame.Y, frame.Bottom - minSize);
        double w = Math.Clamp(result.Width, minSize, frame.Right - x);
        double h = Math.Clamp(result.Height, minSize, frame.Bottom - y);

        _cropPreviewRect = new Rect(x, y, w, h);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            // Zoom to cursor: keep the canvas point under the pointer fixed on screen.
            var screenPos = e.GetPosition(this);
            var canvasPtBefore = ScreenToCanvas(screenPos);

            _zoom = Math.Clamp(Math.Round(_zoom * (1.0 + e.Delta.Y * 0.08), 3), MinZoom, MaxZoom);
            _isFitModeActive = false;

            double centerOffsetX = (Bounds.Width - _logicalWidth * _zoom) / 2;
            double centerOffsetY = (Bounds.Height - _logicalHeight * _zoom) / 2;
            _panOffset = new Point(
                screenPos.X - canvasPtBefore.X * _zoom - centerOffsetX,
                screenPos.Y - canvasPtBefore.Y * _zoom - centerOffsetY);

            ZoomChanged?.Invoke(_zoom);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _panOffset = new Point(_panOffset.X + e.Delta.Y * 40, _panOffset.Y);
            _isFitModeActive = false;
        }
        else
        {
            _panOffset = new Point(_panOffset.X, _panOffset.Y + e.Delta.Y * 40);
            _isFitModeActive = false;
        }

        // Every branch above sets _panOffset directly with no bound — unlike OnSizeChanged,
        // which already clamped after its own pan-affecting change. Without this, repeated
        // scrolling pushes the pan arbitrarily far, and the canvas's own hand-drawn content
        // (Render() draws at whatever the viewport matrix says, with no automatic clip back to
        // this control's own arranged bounds) ends up rendering outside its Grid cell —
        // including over the rows above it.
        ClampPanOffset();

        // Every branch above moves the screen-space mapping of canvas content (pan or zoom-to-
        // cursor) — CanvasChanged is the one signal external listeners (e.g. a floating chrome
        // element anchored to the selection) need regardless of which branch ran.
        CanvasChanged?.Invoke();
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_isFitModeActive) FitToWindow(); // raises ZoomChanged itself, which listeners also key off
        else
        {
            ClampPanOffset();
            CanvasChanged?.Invoke();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Space && !_spaceHeld)
        {
            _spaceHeld = true;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_isCropModeActive) { CancelCrop(); e.Handled = true; return; }
            if (_isPlacingShape) { _isPlacingShape = false; InvalidateVisual(); e.Handled = true; return; }
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down
            && _selection.Count > 0 && !_isCropModeActive && !_isPlacingShape)
        {
            double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            double dx = e.Key switch { Key.Left => -step, Key.Right => step, _ => 0 };
            double dy = e.Key switch { Key.Up => -step, Key.Down => step, _ => 0 };

            var nudgeable = _selection.Where(el => !el.IsLocked).ToList();
            if (nudgeable.Count == 0) return;

            _nudgeOrigins ??= nudgeable.ToDictionary(el => el, el => new Point(el.X, el.Y));
            foreach (var el in nudgeable) { el.X += dx; el.Y += dy; }
            CanvasChanged?.Invoke();
            InvalidateVisual();

            _nudgeCommitTimer.Stop();
            _nudgeCommitTimer.Start();
            e.Handled = true;
        }
    }

    private void OnNudgeCommitTick(object? sender, EventArgs e)
    {
        _nudgeCommitTimer.Stop();
        var origins = _nudgeOrigins;
        _nudgeOrigins = null;
        if (origins == null) return;

        var moves = new List<(CanvasElement Element, Point OldPos, Point NewPos)>();
        foreach (var (el, oldPos) in origins)
        {
            var newPos = new Point(el.X, el.Y);
            if (oldPos != newPos) moves.Add((el, oldPos, newPos));
        }
        if (moves.Count > 0) ElementsMoved?.Invoke(moves);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
            e.Handled = true;
        }
    }

    /// <summary>Flattens the canvas (background + all elements, ignoring UI-only affordances
    /// like the checkerboard/grid/handles) into an offscreen bitmap at logical resolution and
    /// reads back the single pixel at the given canvas-space point.</summary>
    public Color? SampleColorAtCanvasPoint(Point canvasPt)
    {
        int px = (int)Math.Floor(canvasPt.X);
        int py = (int)Math.Floor(canvasPt.Y);
        if (px < 0 || py < 0 || px >= _logicalWidth || py >= _logicalHeight) return null;

        var pixelSize = new PixelSize((int)Math.Ceiling(_logicalWidth), (int)Math.Ceiling(_logicalHeight));
        using var rtb = new RenderTargetBitmap(pixelSize);
        using (var dc = rtb.CreateDrawingContext())
        {
            var canvasRect = new Rect(0, 0, _logicalWidth, _logicalHeight);
            if (_backgroundColor.A > 0)
            {
                dc.DrawRectangle(new SolidColorBrush(_backgroundColor), null, canvasRect);
            }
            foreach (var el in _elements.OrderBy(e => e.ZIndex))
            {
                DrawElement(dc, el);
            }
        }

        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            rtb.CopyPixels(new PixelRect(px, py, 1, 1), buffer, 4, 4);
            var bytes = new byte[4];
            Marshal.Copy(buffer, bytes, 0, 4);
            // Bgra8888 byte order (matches this codebase's existing SKColorType.Bgra8888 usage).
            byte a = bytes[3];
            return a == 0 ? Colors.Transparent : Color.FromArgb(a, bytes[2], bytes[1], bytes[0]);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Exposes the viewport transform so the view's code-behind can position an
    /// overlay control (the inline text-edit TextBox) directly over a canvas-space rect.</summary>
    public Rect CanvasToScreenRect(Rect canvasRect)
    {
        var m = GetViewportMatrix();
        var topLeft = canvasRect.TopLeft.Transform(m);
        var bottomRight = canvasRect.BottomRight.Transform(m);
        return new Rect(topLeft, bottomRight);
    }

    /// <summary>Screen-space bounding rect of the current selection (single or multi), or null
    /// when nothing is selected. The viewport transform is a pure scale+translate (no
    /// rotation), so unioning each element's own rotated bounds in canvas-space first and
    /// converting once is equivalent to converting-then-unioning — same math
    /// <see cref="ComputeGroupBounds"/> uses for the multi-selection outline.</summary>
    public Rect? GetSelectionScreenBounds()
    {
        if (_selection.Count == 0) return null;
        var bounds = _selection[0].GetRotatedBounds();
        for (int i = 1; i < _selection.Count; i++) bounds = bounds.Union(_selection[i].GetRotatedBounds());
        return CanvasToScreenRect(bounds);
    }

    /// <summary>Where a fixed-size piece of floating chrome (e.g. a quick-actions cluster)
    /// should sit relative to a selection's screen-space bounds: horizontally centered and
    /// clamped so it never renders off either side of the visible viewport; prefers directly
    /// above the selection but flips to below when there isn't room (e.g. the selection sits
    /// flush against the top of the canvas) — the collision-avoidance the reference screenshots'
    /// floating toolbar was missing.</summary>
    public Point GetFloatingChromeAnchor(Rect selectionScreenBounds, Size chromeSize, double gap = 26)
    {
        double x = selectionScreenBounds.Center.X - chromeSize.Width / 2;
        x = Math.Clamp(x, 0, Math.Max(0, Bounds.Width - chromeSize.Width));

        double yAbove = selectionScreenBounds.Y - chromeSize.Height - gap;
        double y = yAbove >= 0 ? yAbove : selectionScreenBounds.Bottom + gap;

        return new Point(x, y);
    }

    private void ApplyResize(double dx, double dy, bool shiftHeld)
    {
        if (_selection.Count != 1) return;
        var selected = _selection[0];

        double? lockedAspectRatio = (selected.AspectLocked ^ shiftHeld) && selected.GetAspectRatio() > 0
            ? selected.GetAspectRatio()
            : null;

        var orig = new Rect(_origX, _origY, _origW, _origH);
        var result = ComputeResizedRect(orig, _resizeHandle, dx, dy, 20, lockedAspectRatio);

        selected.X = result.X;
        selected.Y = result.Y;
        selected.Width = result.Width;
        selected.Height = result.Height;
    }

    /// <summary>
    /// Pure resize math shared by single-element resize, group-resize, and fixed-aspect-ratio
    /// crop presets. Handle indices: 0=TL 1=T 2=TR 3=R 4=BR 5=B 6=BL 7=L.
    /// </summary>
    private static Rect ComputeResizedRect(Rect orig, int handleIndex, double dx, double dy, double minSize, double? lockedAspectRatio)
    {
        double origX = orig.X, origY = orig.Y, origW = orig.Width, origH = orig.Height;
        double newX = origX, newY = origY, newW = origW, newH = origH;

        switch (handleIndex)
        {
            case 0: // TL
                newX = Math.Min(origX + dx, origX + origW - minSize);
                newY = Math.Min(origY + dy, origY + origH - minSize);
                newW = Math.Max(origW - dx, minSize);
                newH = Math.Max(origH - dy, minSize);
                break;
            case 1: // T
                newY = Math.Min(origY + dy, origY + origH - minSize);
                newH = Math.Max(origH - dy, minSize);
                break;
            case 2: // TR
                newY = Math.Min(origY + dy, origY + origH - minSize);
                newW = Math.Max(origW + dx, minSize);
                newH = Math.Max(origH - dy, minSize);
                break;
            case 3: // R
                newW = Math.Max(origW + dx, minSize);
                break;
            case 4: // BR
                newW = Math.Max(origW + dx, minSize);
                newH = Math.Max(origH + dy, minSize);
                break;
            case 5: // B
                newH = Math.Max(origH + dy, minSize);
                break;
            case 6: // BL
                newX = Math.Min(origX + dx, origX + origW - minSize);
                newW = Math.Max(origW - dx, minSize);
                newH = Math.Max(origH + dy, minSize);
                break;
            case 7: // L
                newX = Math.Min(origX + dx, origX + origW - minSize);
                newW = Math.Max(origW - dx, minSize);
                break;
        }

        // Aspect-ratio lock: constrain the drag, re-anchoring so the OPPOSITE corner/edge
        // stays pinned.
        if (lockedAspectRatio is { } ratio && ratio > 0)
        {
            switch (handleIndex)
            {
                case 1: case 5: // pure T/B: height drives width, anchored horizontally already
                    newW = newH * ratio;
                    newX = origX + (origW - newW) / 2;
                    break;
                case 3: case 7: // pure L/R: width drives height, anchored vertically already
                    newH = newW / ratio;
                    newY = origY + (origH - newH) / 2;
                    break;
                default: // corner handles: drive from the larger proportional change
                    double widthRatio = Math.Abs(newW / origW - 1);
                    double heightRatio = Math.Abs(newH / origH - 1);
                    if (widthRatio >= heightRatio) newH = newW / ratio; else newW = newH * ratio;

                    switch (handleIndex)
                    {
                        case 0: newX = origX + origW - newW; newY = origY + origH - newH; break; // TL anchors BR
                        case 2: newY = origY + origH - newH; break; // TR anchors BL
                        case 6: newX = origX + origW - newW; break; // BL anchors TR
                                                                     // case 4 (BR) anchors TL — X/Y already correct
                    }
                    break;
            }
        }

        return new Rect(newX, newY, newW, newH);
    }
}
