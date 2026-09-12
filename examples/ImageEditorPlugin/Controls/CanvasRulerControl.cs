using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PdfEditorApp.Core.Models;

namespace PdfEditorApp.Plugins.ImageEditor.Controls;

public class CanvasRulerControl : Control
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<CanvasRulerControl, Orientation>(nameof(Orientation), Orientation.Horizontal);

    public static readonly StyledProperty<RulerUnit> UnitProperty =
        AvaloniaProperty.Register<CanvasRulerControl, RulerUnit>(nameof(Unit), RulerUnit.Points);

    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<CanvasRulerControl, double>(nameof(ZoomLevel), 1.0);

    public static readonly StyledProperty<double> CursorPositionProperty =
        AvaloniaProperty.Register<CanvasRulerControl, double>(nameof(CursorPosition), -1);

    public static readonly StyledProperty<double> PageOffsetProperty =
        AvaloniaProperty.Register<CanvasRulerControl, double>(nameof(PageOffset), 0.0);

    public static readonly StyledProperty<double> PageDimensionProperty =
        AvaloniaProperty.Register<CanvasRulerControl, double>(nameof(PageDimension), 800.0);

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public RulerUnit Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public double CursorPosition
    {
        get => GetValue(CursorPositionProperty);
        set => SetValue(CursorPositionProperty, value);
    }

    public double PageOffset
    {
        get => GetValue(PageOffsetProperty);
        set => SetValue(PageOffsetProperty, value);
    }

    public double PageDimension
    {
        get => GetValue(PageDimensionProperty);
        set => SetValue(PageDimensionProperty, value);
    }

    // Default Fallback Resources
    private static readonly IBrush s_defBgBrush = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IPen s_defBorderPen = new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 1);
    private static readonly IPen s_defTickPen = new Pen(new SolidColorBrush(Color.Parse("#94A3B8")), 1);
    private static readonly IPen s_defMajorTickPen = new Pen(new SolidColorBrush(Color.Parse("#64748B")), 1);
    private static readonly IPen s_defPageBorderPen = new Pen(new SolidColorBrush(Color.Parse("#CBD5E1")), 1.5);
    private static readonly IPen s_cursorPen = new Pen(new SolidColorBrush(Color.Parse("#0F6CBD")), 1.5);
    private static readonly IBrush s_defPageShadeBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush s_defLabelBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly Typeface s_typeface = new("Segoe UI, -apple-system, sans-serif");

    static CanvasRulerControl()
    {
        AffectsRender<CanvasRulerControl>(
            OrientationProperty,
            UnitProperty,
            ZoomLevelProperty,
            CursorPositionProperty,
            PageOffsetProperty,
            PageDimensionProperty);
    }

    private Pen? _borderPen;
    private Pen? _tickPen;
    private Pen? _majorTickPen;

    private readonly Dictionary<(string Text, double Size), FormattedText> _labelCache = new();
    private IBrush? _labelCacheBrush;

    private static Pen GetCachedPen(ref Pen? cached, IBrush? brush)
    {
        if (cached == null || !ReferenceEquals(cached.Brush, brush))
        {
            cached = new Pen(brush, 1);
        }

        return cached;
    }

    private FormattedText GetCachedLabel(string label, IBrush? labelBrush, double fontSize = 8.5)
    {
        if (!ReferenceEquals(_labelCacheBrush, labelBrush))
        {
            _labelCache.Clear();
            _labelCacheBrush = labelBrush;
        }

        var key = (label, fontSize);
        if (_labelCache.TryGetValue(key, out var cached)) return cached;

        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            s_typeface,
            fontSize,
            labelBrush);

        if (_labelCache.Count >= MaxCachedLabels)
        {
            _labelCache.Clear();
        }

        _labelCache[key] = text;
        return text;
    }

    private const int MaxCachedLabels = 256;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        bool isHorizontal = Orientation == Orientation.Horizontal;

        var bgBrush = (this.TryFindResource("WinInputBgBrush", out var bgObj) && bgObj is IBrush b1) ? b1 : s_defBgBrush;
        var borderBrush = (this.TryFindResource("WinBorderBrush", out var bdrObj) && bdrObj is IBrush b2) ? b2 : s_defBorderPen.Brush;
        var tickBrush = (this.TryFindResource("WinSubtleBrush", out var tObj) && tObj is IBrush b3) ? b3 : s_defTickPen.Brush;
        var majorTickBrush = (this.TryFindResource("WinMutedBrush", out var mtObj) && mtObj is IBrush b4) ? b4 : s_defMajorTickPen.Brush;

        var borderPen = GetCachedPen(ref _borderPen, borderBrush);
        var tickPen = GetCachedPen(ref _tickPen, tickBrush);
        var majorTickPen = GetCachedPen(ref _majorTickPen, majorTickBrush);
        var pageShadeBrush = (this.TryFindResource("WinPanelBrush", out var psObj) && psObj is IBrush b5) ? b5 : s_defPageShadeBrush;
        var labelBrush = majorTickBrush ?? s_defLabelBrush;

        context.FillRectangle(bgBrush, new Rect(0, 0, bounds.Width, bounds.Height));

        if (isHorizontal)
        {
            context.DrawLine(borderPen, new Point(0, bounds.Height), new Point(bounds.Width, bounds.Height));

            double zoom = Math.Max(0.1, ZoomLevel);
            double stepPts = Unit == RulerUnit.Inches ? 72.0 : (Unit == RulerUnit.Millimeters ? 72.0 / 2.54 : 50.0);
            double minorStepPts = stepPts / 5.0;

            double startX = PageOffset;
            double endX = startX + (PageDimension * zoom);

            context.FillRectangle(pageShadeBrush, new Rect(Math.Max(0, startX), 0, Math.Max(0, endX - Math.Max(0, startX)), bounds.Height));
            context.DrawLine(borderPen, new Point(startX, 0), new Point(startX, bounds.Height));
            context.DrawLine(borderPen, new Point(endX, 0), new Point(endX, bounds.Height));

            double maxPts = PageDimension + 200;
            for (double pt = 0; pt <= maxPts; pt += minorStepPts)
            {
                double x = startX + (pt * zoom);
                if (x < 0 || x > bounds.Width) continue;

                bool isMajor = Math.Abs(pt % stepPts) < 0.01;
                double tickH = isMajor ? bounds.Height * 0.55 : bounds.Height * 0.25;

                context.DrawLine(isMajor ? majorTickPen : tickPen, new Point(x, bounds.Height - tickH), new Point(x, bounds.Height));

                if (isMajor)
                {
                    string label = Unit switch
                    {
                        RulerUnit.Inches => $"{pt / 72.0:0}\"",
                        RulerUnit.Millimeters => $"{pt * 25.4 / 72.0:0}",
                        _ => $"{pt:0}"
                    };

                    context.DrawText(GetCachedLabel(label, labelBrush), new Point(x + 2, 2));
                }
            }

            if (CursorPosition >= 0)
            {
                double cursorX = startX + (CursorPosition * zoom);
                if (cursorX >= 0 && cursorX <= bounds.Width)
                {
                    context.DrawLine(s_cursorPen, new Point(cursorX, 0), new Point(cursorX, bounds.Height));
                }
            }
        }
        else
        {
            context.DrawLine(borderPen, new Point(bounds.Width, 0), new Point(bounds.Width, bounds.Height));

            double zoom = Math.Max(0.1, ZoomLevel);
            double stepPts = Unit == RulerUnit.Inches ? 72.0 : (Unit == RulerUnit.Millimeters ? 72.0 / 2.54 : 50.0);
            double minorStepPts = stepPts / 5.0;

            double startY = PageOffset;
            double endY = startY + (PageDimension * zoom);

            context.FillRectangle(pageShadeBrush, new Rect(0, Math.Max(0, startY), bounds.Width, Math.Max(0, endY - Math.Max(0, startY))));
            context.DrawLine(borderPen, new Point(0, startY), new Point(bounds.Width, startY));
            context.DrawLine(borderPen, new Point(0, endY), new Point(bounds.Width, endY));

            double maxPts = PageDimension + 200;
            for (double pt = 0; pt <= maxPts; pt += minorStepPts)
            {
                double y = startY + (pt * zoom);
                if (y < 0 || y > bounds.Height) continue;

                bool isMajor = Math.Abs(pt % stepPts) < 0.01;
                double tickW = isMajor ? bounds.Width * 0.55 : bounds.Width * 0.25;

                context.DrawLine(isMajor ? majorTickPen : tickPen, new Point(bounds.Width - tickW, y), new Point(bounds.Width, y));

                if (isMajor)
                {
                    string label = Unit switch
                    {
                        RulerUnit.Inches => $"{pt / 72.0:0}\"",
                        RulerUnit.Millimeters => $"{pt * 25.4 / 72.0:0}",
                        _ => $"{pt:0}"
                    };

                    context.DrawText(GetCachedLabel(label, labelBrush, fontSize: 8.0), new Point(2, y + 2));
                }
            }

            if (CursorPosition >= 0)
            {
                double cursorY = startY + (CursorPosition * zoom);
                if (cursorY >= 0 && cursorY <= bounds.Height)
                {
                    context.DrawLine(s_cursorPen, new Point(0, cursorY), new Point(bounds.Width, cursorY));
                }
            }
        }
    }
}
