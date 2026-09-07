using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace PdfEditorApp.Plugins.MusicPlayer;

/// <summary>
/// A Material You / Android 13/14 inspired Wavy Seek Slider.
/// The played portion of the track renders as a smooth, animated sine wave (curvy line)
/// that dynamically undulates while playing and tapers into the thumb.
/// The unplayed portion renders as a clean straight horizontal track.
/// Full click-to-seek and drag-to-scrub supported anywhere across the entire slider area.
/// </summary>
public class WavySlider : Slider
{
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<WavySlider, bool>(nameof(IsPlaying), defaultValue: false);

    public static readonly StyledProperty<double> WaveAmplitudeProperty =
        AvaloniaProperty.Register<WavySlider, double>(nameof(WaveAmplitude), defaultValue: 4.0);

    public static readonly StyledProperty<double> WaveLengthProperty =
        AvaloniaProperty.Register<WavySlider, double>(nameof(WaveLength), defaultValue: 24.0);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<WavySlider, double>(nameof(StrokeThickness), defaultValue: 3.5);

    public static readonly StyledProperty<IBrush?> UnplayedBrushProperty =
        AvaloniaProperty.Register<WavySlider, IBrush?>(nameof(UnplayedBrush));

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public double WaveAmplitude
    {
        get => GetValue(WaveAmplitudeProperty);
        set => SetValue(WaveAmplitudeProperty, value);
    }

    public double WaveLength
    {
        get => GetValue(WaveLengthProperty);
        set => SetValue(WaveLengthProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? UnplayedBrush
    {
        get => GetValue(UnplayedBrushProperty);
        set => SetValue(UnplayedBrushProperty, value);
    }

    private Track? _track;
    private Thumb? _thumb;
    private DispatcherTimer? _animationTimer;
    private double _phase;
    private bool _isInteracting;

    public WavySlider()
    {
        Background = Brushes.Transparent;

        // Tunneling pointer event handlers: capture clicks/drags anywhere on the entire progress bar
        AddHandler(PointerPressedEvent, OnTunnelPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTunnelPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnTunnelPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnTunnelPointerCaptureLost, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        // Redraw on layout or size change
        AffectsRender<WavySlider>(
            IsPlayingProperty,
            WaveAmplitudeProperty,
            WaveLengthProperty,
            StrokeThicknessProperty,
            UnplayedBrushProperty);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _track = e.NameScope.Find<Track>("PART_Track");
        _thumb = _track?.Thumb ?? e.NameScope.Find<Thumb>("thumb");
    }

    private void OnTunnelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        e.Pointer.Capture(this);
        _isInteracting = true;

        double targetSeconds = CalculateTargetSeconds(point.Position.X);
        Value = targetSeconds;

        var vm = DataContext as MusicPlayerViewModel;
        vm?.BeginSeek();
        vm?.UpdateSeek(targetSeconds);

        InvalidateVisual();
        e.Handled = true;
    }

    private void OnTunnelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isInteracting) return;

        var point = e.GetCurrentPoint(this);
        double targetSeconds = CalculateTargetSeconds(point.Position.X);
        Value = targetSeconds;

        var vm = DataContext as MusicPlayerViewModel;
        vm?.UpdateSeek(targetSeconds);

        InvalidateVisual();
        e.Handled = true;
    }

    private void OnTunnelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isInteracting) return;

        _isInteracting = false;
        e.Pointer.Capture(null);

        var point = e.GetCurrentPoint(this);
        double targetSeconds = CalculateTargetSeconds(point.Position.X);
        Value = targetSeconds;

        var vm = DataContext as MusicPlayerViewModel;
        vm?.CommitSeek(targetSeconds);

        InvalidateVisual();
        e.Handled = true;
    }

    private void OnTunnelPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isInteracting) return;

        _isInteracting = false;
        var vm = DataContext as MusicPlayerViewModel;
        vm?.CommitSeek(Value);
        InvalidateVisual();
    }

    private double CalculateTargetSeconds(double pointerX)
    {
        double stroke = StrokeThickness;
        double leftX = stroke + 2.0;
        double rightX = Bounds.Width - stroke - 2.0;
        if (rightX <= leftX) return Minimum;

        double ratio = Math.Clamp((pointerX - leftX) / (rightX - leftX), 0.0, 1.0);
        double range = Maximum - Minimum;
        if (range <= 0.0 || double.IsNaN(range)) return Minimum;

        return Minimum + (ratio * range);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsPlayingProperty)
        {
            UpdateAnimationState();
        }
        else if (change.Property == ValueProperty ||
                 change.Property == MinimumProperty ||
                 change.Property == MaximumProperty ||
                 change.Property == BoundsProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateAnimationState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopAnimation();
    }

    private void UpdateAnimationState()
    {
        if (IsPlaying && VisualRoot != null)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    private void StartAnimation()
    {
        if (_animationTimer == null)
        {
            _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS liquid ripple
            };
            _animationTimer.Tick += OnAnimationTick;
        }

        if (!_animationTimer.IsEnabled)
        {
            _animationTimer.Start();
        }
    }

    private void StopAnimation()
    {
        if (_animationTimer?.IsEnabled == true)
        {
            _animationTimer.Stop();
            InvalidateVisual();
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        // Smoothly advance phase while music is actively playing
        _phase = (_phase + 0.12) % (Math.PI * 2.0);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        double centerY = bounds.Height / 2.0;
        double stroke = StrokeThickness;

        double min = Minimum;
        double max = Maximum;
        double val = Value;
        double range = max - min;
        double ratio = range > 0.0 ? Math.Clamp((val - min) / range, 0.0, 1.0) : 0.0;
        if (double.IsNaN(ratio) || double.IsInfinity(ratio)) ratio = 0.0;

        double leftX = stroke + 2.0;
        double rightX = bounds.Width - stroke - 2.0;
        if (rightX <= leftX) return;

        double availableWidth = rightX - leftX;

        // The thumb X position is always in lockstep with the current ratio
        double thumbX = leftX + (ratio * availableWidth);

        // 1. Draw unplayed straight line (from thumbX to rightX)
        if (thumbX < rightX)
        {
            var unplayedBrush = UnplayedBrush ?? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
            var unplayedPen = new Pen(unplayedBrush, stroke, lineCap: PenLineCap.Round);
            context.DrawLine(unplayedPen, new Point(thumbX, centerY), new Point(rightX, centerY));
        }

        // 2. Draw played wavy sine curve (from leftX to thumbX)
        if (thumbX > leftX)
        {
            var playedBrush = Foreground ?? new SolidColorBrush(Color.FromRgb(30, 94, 235));
            var playedPen = new Pen(playedBrush, stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            var geometry = new StreamGeometry();
            using (var sgc = geometry.Open())
            {
                double waveLen = Math.Max(12.0, WaveLength);
                double baseAmp = WaveAmplitude;
                double step = 2.0;

                sgc.BeginFigure(new Point(leftX, centerY), false);

                double playedDist = thumbX - leftX;
                for (double x = leftX; x <= thumbX; x += step)
                {
                    // Smooth envelope tapering near ends so wave starts and joins thumb at centerY
                    double distFromStart = x - leftX;
                    double distFromEnd = thumbX - x;
                    double taperStart = Math.Clamp(distFromStart / 14.0, 0.0, 1.0);
                    double taperEnd = Math.Clamp(distFromEnd / 16.0, 0.0, 1.0);
                    double envelope = Math.Min(taperStart, taperEnd);

                    double y = centerY + (baseAmp * envelope * Math.Sin((2.0 * Math.PI * (x - leftX) / waveLen) - _phase));
                    sgc.LineTo(new Point(x, y));
                }

                sgc.LineTo(new Point(thumbX, centerY));
                sgc.EndFigure(false);
            }

            context.DrawGeometry(null, playedPen, geometry);
        }

        // Render thumb on top
        base.Render(context);
    }
}
