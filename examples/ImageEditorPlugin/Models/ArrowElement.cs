using Avalonia;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>
/// A line with an arrowhead at the end point.
/// Positioned via X,Y as the start point; Width/Height encode the end offset.
/// </summary>
public class ArrowElement : CanvasElement, IHasStroke
{
    public override string ElementType => "Arrow";

    public Color StrokeColor { get; set; } = Color.FromArgb(255, 251, 191, 36); // amber
    public double StrokeThickness { get; set; } = 2.5;
    public StrokeDashStyle DashStyle { get; set; } = StrokeDashStyle.Solid;
    public double ArrowHeadSize { get; set; } = 12;

    /// <summary>Width/Height encode a signed end-point offset, not a size — "aspect ratio"
    /// isn't a meaningful concept for a direction vector. Returning 0 (not &gt; 0) tells every
    /// aspect-lock call site (single/group resize) to never lock an arrow's resize, regardless
    /// of Shift or AspectLocked — the base class's Width/Height-ratio fallback would otherwise
    /// force a horizontal/vertical arrow (Height or Width == 0) to bend diagonally on resize.</summary>
    public override double GetAspectRatio() => 0;

    public ArrowElement()
    {
        Width = 100;   // end point X offset from X
        Height = 0;    // end point Y offset from Y
    }

    /// <summary>Start point of the arrow.</summary>
    public Point Start => new(X, Y);

    /// <summary>End point of the arrow.</summary>
    public Point End => new(X + Width, Y + Height);

    public override Rect Bounds
    {
        get
        {
            double minX = Math.Min(X, X + Width);
            double minY = Math.Min(Y, Y + Height);
            double maxX = Math.Max(X, X + Width);
            double maxY = Math.Max(Y, Y + Height);
            // Inflate slightly for handle hit-testing
            return new Rect(minX - 8, minY - 8, maxX - minX + 16, maxY - minY + 16);
        }
    }

    public override void Draw(DrawingContext dc)
    {
        var pen = new Pen(new SolidColorBrush(StrokeColor), StrokeThickness, ShapeDashStyles.ToAvaloniaDashStyle(DashStyle))
        {
            LineCap = PenLineCap.Round
        };

        var start = Start;
        var end = End;

        // Draw the shaft
        dc.DrawLine(pen, start, end);

        // Draw arrowhead (two lines from the end)
        var dir = end - start;
        double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (len < 1) return;

        var unit = new Vector(dir.X / len, dir.Y / len);
        var perp = new Vector(-unit.Y, unit.X);

        double hs = ArrowHeadSize;
        var head1 = end - new Vector(unit.X * hs + perp.X * hs * 0.4,
                                     unit.Y * hs + perp.Y * hs * 0.4);
        var head2 = end - new Vector(unit.X * hs - perp.X * hs * 0.4,
                                     unit.Y * hs - perp.Y * hs * 0.4);

        dc.DrawLine(pen, end, head1);
        dc.DrawLine(pen, end, head2);
    }

    public override bool HitTest(Point pt)
    {
        // Hit test: distance from point to line segment <= threshold
        const double threshold = 6;
        var a = Start;
        var b = End;
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 0.0001)
        {
            double d0 = Math.Sqrt((pt.X - a.X) * (pt.X - a.X) + (pt.Y - a.Y) * (pt.Y - a.Y));
            return d0 <= threshold;
        }

        double t = ((pt.X - a.X) * dx + (pt.Y - a.Y) * dy) / lenSq;
        t = Math.Clamp(t, 0, 1);
        double projX = a.X + t * dx;
        double projY = a.Y + t * dy;
        double dist = Math.Sqrt((pt.X - projX) * (pt.X - projX) + (pt.Y - projY) * (pt.Y - projY));
        return dist <= threshold;
    }

    public override CanvasElement Clone() => new ArrowElement
    {
        X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        RotationDegrees = RotationDegrees, FlipX = FlipX, FlipY = FlipY,
        Opacity = Opacity, IsVisible = IsVisible, IsLocked = false, Name = Name, GroupId = null,
        AspectLocked = AspectLocked,
        StrokeColor = StrokeColor, StrokeThickness = StrokeThickness, DashStyle = DashStyle,
        ArrowHeadSize = ArrowHeadSize,
    };
}
