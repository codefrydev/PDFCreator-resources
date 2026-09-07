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

        // Keep the canvas's tool mode in sync with the toolbar's selected tool.
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ImageEditorViewModel.ActiveToolMode))
            {
                CanvasControl.ActiveToolMode = vm.ActiveToolMode;
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

    // ── Inline double-click text editing ────────────────────────────────────
    private Models.TextElement? _editingTextElement;

    private void BeginInlineTextEdit(Models.TextElement textEl)
    {
        _editingTextElement = textEl;
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

        if (el != null && !cancel && DataContext is ImageEditorViewModel vm)
        {
            vm.CommitTextEdit(el, InlineTextEditBox.Text ?? string.Empty);
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
            "Text" => "FormatText",
            "Rectangle" => "RectangleOutline",
            "Ellipse" => "EllipseOutline",
            "Arrow" => "ArrowTopRight",
            "Image" => "ImageOutline",
            _ => "ShapeOutline"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Maps CanvasElement.IsLocked to a Lock/LockOpen icon kind.</summary>
public class IsLockedToIconConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IsLockedToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "Lock" : "LockOpenOutline";

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Maps CanvasElement.IsVisible to Eye/EyeOff icon kind.</summary>
public class IsVisibleToIconConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IsVisibleToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is false ? "EyeOffOutline" : "EyeOutline";

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts an Avalonia Color to a SolidColorBrush for UI swatches and borders.</summary>
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

