using System;
using Avalonia;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>A filled/stroked N-pointed star inscribed in the element's bounding box, first
/// point at the top — same fill+stroke pattern as PolygonElement, but alternates outer/inner
/// radius per vertex instead of a constant radius.</summary>
public class StarElement : CanvasElement, IHasFill, IHasStroke
{
    public override string ElementType => "Star";

    public Color FillColor { get; set; } = Color.FromArgb(180, 250, 204, 21); // amber
    public GradientFill? Gradient { get; set; }
    public Color StrokeColor { get; set; } = Color.FromArgb(255, 161, 98, 7);
    public double StrokeThickness { get; set; } = 2;
    public StrokeDashStyle DashStyle { get; set; } = StrokeDashStyle.Solid;

    /// <summary>Number of outer points — 5 is the classic star.</summary>
    public int PointCount { get; set; } = 5;

    /// <summary>Inner-vertex radius as a fraction of the outer radius (0..1) — smaller values
    /// make sharper points.</summary>
    public double InnerRadiusRatio { get; set; } = 0.5;

    public StarElement()
    {
        Width = 100;
        Height = 100;
    }

    private Point[] ComputeVertices()
    {
        int n = Math.Max(3, PointCount);
        var points = new Point[n * 2];
        double cx = X + Width / 2, cy = Y + Height / 2;
        double outerRx = Width / 2, outerRy = Height / 2;
        double innerRx = outerRx * InnerRadiusRatio, innerRy = outerRy * InnerRadiusRatio;
        double step = Math.PI / n;
        const double startAngle = -Math.PI / 2; // first outer point at top
        for (int i = 0; i < n * 2; i++)
        {
            double angle = startAngle + i * step;
            bool isOuter = i % 2 == 0;
            double rx = isOuter ? outerRx : innerRx;
            double ry = isOuter ? outerRy : innerRy;
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

    public override CanvasElement Clone() => new StarElement
    {
        X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        RotationDegrees = RotationDegrees, FlipX = FlipX, FlipY = FlipY,
        Opacity = Opacity, IsVisible = IsVisible, IsLocked = false, Name = Name, GroupId = null,
        AspectLocked = AspectLocked,
        FillColor = FillColor, Gradient = Gradient, StrokeColor = StrokeColor,
        StrokeThickness = StrokeThickness, DashStyle = DashStyle,
        PointCount = PointCount, InnerRadiusRatio = InnerRadiusRatio,
    };
}
