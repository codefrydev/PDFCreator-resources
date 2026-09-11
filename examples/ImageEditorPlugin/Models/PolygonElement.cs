using System;
using Avalonia;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>A filled/stroked regular convex polygon (triangle, pentagon, hexagon, ...)
/// inscribed in the element's bounding box, first vertex at the top — follows
/// RectangleElement's fill+stroke pattern exactly, just with a computed vertex set instead of
/// a rounded rect.</summary>
public class PolygonElement : CanvasElement, IHasFill, IHasStroke
{
    public override string ElementType => "Polygon";

    public Color FillColor { get; set; } = Color.FromArgb(180, 34, 197, 94); // green
    public GradientFill? Gradient { get; set; }
    public Color StrokeColor { get; set; } = Color.FromArgb(255, 21, 128, 61);
    public double StrokeThickness { get; set; } = 2;
    public StrokeDashStyle DashStyle { get; set; } = StrokeDashStyle.Solid;

    /// <summary>Number of sides — 3 = triangle, 5 = pentagon, 6 = hexagon, etc.</summary>
    public int SideCount { get; set; } = 3;

    public PolygonElement()
    {
        Width = 100;
        Height = 100;
    }

    private Point[] ComputeVertices()
    {
        int n = Math.Max(3, SideCount);
        var points = new Point[n];
        double cx = X + Width / 2, cy = Y + Height / 2;
        double rx = Width / 2, ry = Height / 2;
        const double startAngle = -Math.PI / 2; // first vertex at top
        for (int i = 0; i < n; i++)
        {
            double angle = startAngle + i * (2 * Math.PI / n);
            points[i] = new Point(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle));
        }
        return points;
    }

    public override void Draw(DrawingContext dc)
    {
        IBrush fill = Gradient?.ToBrush() ?? new SolidColorBrush(FillColor);
        var pen = StrokeThickness > 0
            ? new Pen(new SolidColorBrush(StrokeColor), StrokeThickness, ShapeDashStyles.ToAvaloniaDashStyle(DashStyle))
            : null;
        dc.DrawGeometry(fill, pen, PolygonGeometry.BuildClosedFigure(ComputeVertices()));
    }

    public override bool HitTest(Point pt) => PolygonGeometry.Contains(ComputeVertices(), pt);

    public override CanvasElement Clone() => new PolygonElement
    {
        X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        RotationDegrees = RotationDegrees, FlipX = FlipX, FlipY = FlipY,
        Opacity = Opacity, IsVisible = IsVisible, IsLocked = false, Name = Name, GroupId = null,
        AspectLocked = AspectLocked,
        FillColor = FillColor, Gradient = Gradient, StrokeColor = StrokeColor,
        StrokeThickness = StrokeThickness, DashStyle = DashStyle, SideCount = SideCount,
    };
}
