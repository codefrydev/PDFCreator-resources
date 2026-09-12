using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace PdfEditorApp.Plugins.ImageEditor;

/// <summary>
/// Code-behind for ImageEditorView: keyboard and canvas wiring. Window chrome (drag,
/// minimize, close, resize) is provided automatically by the host's StandardCard overlay
/// chrome — see ImageEditorPlugin.cs's OverlayDescriptor.
/// </summary>
public partial class ImageEditorView : UserControl
{
    public ImageEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ImageEditorViewModel vm) return;

        // Wire canvas control into the ViewModel so it can call Refresh()
        vm.CanvasControl = CanvasControl;

        // Wire canvas selection (single or multi) back to ViewModel
        CanvasControl.SelectionChanged += selection => vm.UpdateSelectionFromCanvas(selection);

        // Keep the zoom-percentage readout in sync with the canvas's own zoom state.
        CanvasControl.ZoomChanged += zoom => vm.ZoomLevel = zoom;

        // Wire cursor position tracking for rulers
        CanvasControl.CursorPositionChanged += pt =>
        {
            vm.CursorCanvasX = pt.X;
            vm.CursorCanvasY = pt.Y;
        };

        void SyncRulerOrigins()
        {
            vm.CanvasOriginScreenX = CanvasControl.CanvasOriginScreenX;
            vm.CanvasOriginScreenY = CanvasControl.CanvasOriginScreenY;
        }

        CanvasControl.CanvasChanged += SyncRulerOrigins;
        CanvasControl.ZoomChanged += _ => SyncRulerOrigins();
        CanvasControl.SizeChanged += (_, _) => SyncRulerOrigins();
        SyncRulerOrigins();

        // Keep the floating quick-actions cluster anchored to the selection whenever anything
        // that could move its screen position happens (selection change, a live drag, pan, or
        // zoom), plus once more as soon as the cluster's own size is first known (it starts at
        // zero size before its first layout pass, so the very first appearance would otherwise
        // anchor from the top-left corner instead of centered for one frame).
        CanvasControl.SelectionChanged += _ => RepositionQuickActionsCluster();
        CanvasControl.CanvasChanged += RepositionQuickActionsCluster;
        CanvasControl.ZoomChanged += _ => RepositionQuickActionsCluster();
        QuickActionsCluster.SizeChanged += (_, _) => RepositionQuickActionsCluster();

        // Move/resize commit into undo history once per completed drag (not per-tick).
        CanvasControl.ElementsMoved += moves => vm.RecordElementsMoved(moves);
        CanvasControl.ElementResized += (el, oldBounds, newBounds) => vm.RecordElementResized(el, oldBounds, newBounds);
        CanvasControl.GroupResized += changes => vm.RecordGroupResized(changes);
        CanvasControl.ElementRotated += (el, before, after) => vm.RecordElementRotated(el, before, after);
        CanvasControl.GroupRotated += (before, after) => vm.RecordGroupRotated(before, after);

        // Click-to-place: canvas reports where the user clicked/dragged for the active tool.
        CanvasControl.PlaceTextRequested += at => vm.PlaceTextElement(at);
        CanvasControl.ShapePlaced += (toolType, start, end) => vm.PlaceShapeElement(toolType, start, end);

        // Crop confirm/reset reports before/after state so the ViewModel can push undo.
        CanvasControl.ElementCropped += (el, before, after) => vm.RecordElementCropped(el, before, after);

        // Eyedropper is one-shot: sample, apply as the primary color, revert to Select.
        CanvasControl.ColorSampled += color =>
        {
            vm.PrimaryColor = color;
            vm.SetToolCommand.Execute("select");
        };

        // Inline double-click text editing: overlay a real TextBox positioned/sized to match
        // the element, live only while editing.
        CanvasControl.TextEditRequested += BeginInlineTextEdit;

        // Keep the canvas's tool mode in sync with the toolbar's selected tool. Also keep the
        // inline text-edit overlay's own formatting in sync with ribbon toggles — without this,
        // clicking Bold/Italic/Alignment while InlineTextEditBox is open updates the underlying
        // model immediately but leaves the visible overlay showing the stale formatting until
        // the user commits (Enter/click-away) and the canvas re-renders from the model.
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ImageEditorViewModel.ActiveToolMode))
            {
                CanvasControl.ActiveToolMode = vm.ActiveToolMode;
            }
            else if (_editingTextElement is { } editing &&
                     (args.PropertyName == nameof(ImageEditorViewModel.IsBold) ||
                      args.PropertyName == nameof(ImageEditorViewModel.IsItalic) ||
                      args.PropertyName == nameof(ImageEditorViewModel.TextAlignmentField)))
            {
                InlineTextEditBox.FontWeight = editing.FontWeight;
                InlineTextEditBox.FontStyle = editing.FontStyle;
                InlineTextEditBox.TextAlignment = editing.Alignment;
            }
        };
        CanvasControl.ActiveToolMode = vm.ActiveToolMode;

        // Initial canvas push
        CanvasControl.SetElements([.. vm.Elements]);
        CanvasControl.SetBackgroundColor(vm.BackgroundColor);
        CanvasControl.SetLogicalCanvasSize(vm.CanvasWidth, vm.CanvasHeight);

        // When element collection changes, refresh canvas
        vm.Elements.CollectionChanged += (_, _) =>
        {
            CanvasControl.SetElements([.. vm.Elements]);
        };
    }

    // ── Keyboard handling ──────────────────────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not ImageEditorViewModel vm) return;

        // Never steal keystrokes from a focused text-entry control (e.g. the text-content
        // TextBox or the font-size NumericUpDown) — otherwise typing/backspacing there would
        // also delete the currently-selected canvas element or trigger undo/redo.
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox or NumericUpDown) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (ctrl && e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            vm.RedoCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.Z)
        {
            vm.UndoCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.Y)
        {
            vm.RedoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.D)
        {
            vm.DuplicateCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.C)
        {
            vm.CopyElementsCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.V)
        {
            vm.PasteElementsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Delete or Key.Back)
        {
            vm.DeleteSelectedElementCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl) return; // avoid stealing other Ctrl+letter shortcuts as tool switches

        // Bold/Italic only act when a TextElement is selected — otherwise fall through so B/I
        // don't accidentally swallow other future single-letter shortcuts.
        if (e.Key == Key.B && vm.SelectedTextElement != null)
        {
            vm.IsBold = !vm.IsBold;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.I && vm.SelectedTextElement != null)
        {
            vm.IsItalic = !vm.IsItalic;
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.V: vm.SetToolCommand.Execute("select"); e.Handled = true; break;
            case Key.T: vm.SetToolCommand.Execute("text"); e.Handled = true; break;
            case Key.R: vm.SetToolCommand.Execute("rect"); e.Handled = true; break;
            case Key.E: vm.SetToolCommand.Execute("ellipse"); e.Handled = true; break;
            case Key.A: vm.SetToolCommand.Execute("arrow"); e.Handled = true; break;
            case Key.Escape: vm.SetToolCommand.Execute("select"); e.Handled = true; break;
        }
    }

    // ── Floating quick-actions cluster ──────────────────────────────────────
    /// <summary>Positions QuickActionsCluster relative to the current selection's screen-space
    /// bounds — a no-op when nothing is selected (the cluster is hidden via its IsVisible
    /// binding in that case, so there's nothing to position).</summary>
    private void RepositionQuickActionsCluster()
    {
        if (CanvasControl.GetSelectionScreenBounds() is not { } selectionBounds) return;

        var anchor = CanvasControl.GetFloatingChromeAnchor(selectionBounds, QuickActionsCluster.Bounds.Size, 26);
        QuickActionsCluster.Margin = new Thickness(anchor.X, anchor.Y, 0, 0);
    }

    // ── Inline double-click text editing ────────────────────────────────────
    private Models.TextElement? _editingTextElement;

    private void BeginInlineTextEdit(Models.TextElement textEl)
    {
        _editingTextElement = textEl;
        if (DataContext is ImageEditorViewModel beginVm) beginVm.IsInlineEditingText = true;

        var screenRect = CanvasControl.CanvasToScreenRect(textEl.Bounds);

        InlineTextEditBox.Margin = new Thickness(screenRect.X, screenRect.Y, 0, 0);
        InlineTextEditBox.Width = Math.Max(screenRect.Width, 20);
        InlineTextEditBox.Height = Math.Max(screenRect.Height, 20);
        InlineTextEditBox.FontSize = Math.Max(textEl.FontSize * CanvasControl.Zoom, 1);
        InlineTextEditBox.FontFamily = textEl.FontFamily;
        InlineTextEditBox.FontWeight = textEl.FontWeight;
        InlineTextEditBox.FontStyle = textEl.FontStyle;
        InlineTextEditBox.Foreground = new SolidColorBrush(textEl.ForegroundColor);
        InlineTextEditBox.Text = textEl.Text;
        InlineTextEditBox.IsVisible = true;
        InlineTextEditBox.Focus();
        InlineTextEditBox.SelectAll();
    }

    private void CommitInlineTextEdit(bool cancel)
    {
        var el = _editingTextElement;
        _editingTextElement = null;
        InlineTextEditBox.IsVisible = false;

        if (DataContext is ImageEditorViewModel vm)
        {
            vm.IsInlineEditingText = false;
            if (el != null && !cancel)
            {
                vm.CommitTextEdit(el, InlineTextEditBox.Text ?? string.Empty);
            }
        }
        CanvasControl.Focus();
    }

    private void InlineTextEditBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            CommitInlineTextEdit(cancel: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CommitInlineTextEdit(cancel: true);
            e.Handled = true;
        }
    }

    private void InlineTextEditBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (_editingTextElement != null) CommitInlineTextEdit(cancel: false);
    }

    // ── Clean teardown ─────────────────────────────────────────────────────
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // Note: Do NOT dispose DataContext here as the view is cached by HomeViewModel
        // for instant tab switching. Teardown is managed by plugin unmount effect.
    }
}

/// <summary>Case-insensitive string equality — used for "is this the active tool/preset" style bindings.</summary>
public class StringEqualsConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>True when the bound value is an <see cref="Models.ImageElement"/> — gates
/// visibility of image-only controls (crop, adjustments, aspect-lock).</summary>
public class IsImageElementConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IsImageElementConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is Models.ImageElement;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Maps CanvasElement.ElementType to a Material Design icon kind.</summary>
public class ElementTypeToIconConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ElementTypeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Text" => Material.Icons.MaterialIconKind.FormatText,
            "Rectangle" => Material.Icons.MaterialIconKind.RectangleOutline,
            "Ellipse" => Material.Icons.MaterialIconKind.EllipseOutline,
            "Arrow" => Material.Icons.MaterialIconKind.ArrowTopRight,
            "Image" => Material.Icons.MaterialIconKind.ImageOutline,
            _ => Material.Icons.MaterialIconKind.ShapeOutline
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public class IsLockedToIconConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IsLockedToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? Material.Icons.MaterialIconKind.LockOutline : Material.Icons.MaterialIconKind.LockOpenOutline;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public class IsVisibleToIconConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IsVisibleToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is false ? Material.Icons.MaterialIconKind.EyeOffOutline : Material.Icons.MaterialIconKind.EyeOutline;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public class ZoomLevelToPercentConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ZoomLevelToPercentConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is double zoom) return $"{(int)Math.Round(zoom * 100)}%";
        return "100%";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public class EqualityToBooleanConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly EqualityToBooleanConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value == null && parameter == null) return true;
        if (value == null || parameter == null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public class BooleanToStringConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BooleanToStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b && parameter is string param)
        {
            var parts = param.Split('|', ':');
            if (parts.Length == 2) return b ? parts[0] : parts[1];
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public class ColorToBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ColorToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is Color c) return new SolidColorBrush(c);
        if (value is string s && Color.TryParse(s, out var parsed)) return new SolidColorBrush(parsed);
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => (value as ISolidColorBrush)?.Color ?? Colors.Transparent;
}

public class ShapePreviewControl : Control
{
    public static readonly StyledProperty<Models.CanvasElement?> ElementProperty =
        AvaloniaProperty.Register<ShapePreviewControl, Models.CanvasElement?>(nameof(Element));

    public Models.CanvasElement? Element
    {
        get => GetValue(ElementProperty);
        set => SetValue(ElementProperty, value);
    }

    static ShapePreviewControl()
    {
        AffectsRender<ShapePreviewControl>(ElementProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Element is not { } el) return;

        var bounds = el.GetRotatedBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        // 0.85 leaves a small margin so a shape's stroke/points aren't clipped at the edge.
        double scale = Math.Min(Bounds.Width / bounds.Width, Bounds.Height / bounds.Height) * 0.85;
        double offsetX = (Bounds.Width - bounds.Width * scale) / 2 - bounds.X * scale;
        double offsetY = (Bounds.Height - bounds.Height * scale) / 2 - bounds.Y * scale;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            el.Draw(context);
        }
    }
}

