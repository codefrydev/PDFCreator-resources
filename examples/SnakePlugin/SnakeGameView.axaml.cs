using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace PdfEditorApp.Plugins.Snake;

public partial class SnakeGameView : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private bool _isLocallyMinimized;

    public SnakeGameView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not SnakeGameViewModel vm) return;

        switch (e.Key)
        {
            case Key.Up:
            case Key.W:
                vm.ChangeDirection(SnakeDirection.Up);
                e.Handled = true;
                break;
            case Key.Down:
            case Key.S:
                vm.ChangeDirection(SnakeDirection.Down);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.A:
                vm.ChangeDirection(SnakeDirection.Left);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.D:
                vm.ChangeDirection(SnakeDirection.Right);
                e.Handled = true;
                break;
            case Key.Space:
                vm.TogglePlayPause();
                e.Handled = true;
                break;
            case Key.R:
                vm.ResetGame();
                vm.StartGame();
                e.Handled = true;
                break;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(this.VisualRoot as Visual ?? this);
            e.Pointer.Capture(sender as Control);
            Focus();
            e.Handled = true;
        }
    }

    private void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging)
        {
            var root = this.VisualRoot as Visual ?? this;
            var currentPoint = e.GetPosition(root);
            var deltaX = currentPoint.X - _dragStartPoint.X;
            var deltaY = currentPoint.Y - _dragStartPoint.Y;
            _dragStartPoint = currentPoint;

            if (FindOverlayViewModel() is { } overlayVm)
            {
                try
                {
                    dynamic d = overlayVm;
                    d.X = Math.Max(0, d.X + deltaX);
                    d.Y = Math.Max(0, d.Y + deltaY);
                }
                catch { }
            }
            e.Handled = true;
        }
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        _isLocallyMinimized = !_isLocallyMinimized;

        if (CanvasContainer != null)
        {
            CanvasContainer.IsVisible = !_isLocallyMinimized;
        }

        if (FooterContainer != null)
        {
            FooterContainer.IsVisible = !_isLocallyMinimized;
        }

        if (FindOverlayViewModel() is { } overlayVm)
        {
            try { ((dynamic)overlayVm).IsMinimized = _isLocallyMinimized; } catch { }
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SnakeGameViewModel vm)
        {
            vm.PauseGame();
        }

        var overlayVm = FindOverlayViewModel();
        if (overlayVm != null)
        {
            try { ((dynamic)overlayVm).Close(); } catch { }
        }
    }

    private object? FindOverlayViewModel()
    {
        // First check parent control DataContext
        Visual? current = this;
        while (current != null)
        {
            if (current is Control ctrl && ctrl.DataContext != null && ctrl.DataContext.GetType().Name.Contains("OverlayInstanceViewModel"))
            {
                return ctrl.DataContext;
            }
            current = current.GetVisualParent();
        }

        return null;
    }
}
