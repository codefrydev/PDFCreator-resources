using Avalonia;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>
/// A filled/stroked ellipse on the canvas.
/// </summary>
public class EllipseElement : CanvasElement, IHasFill, IHasStroke
{
    public override string ElementType => "Ellipse";

    public Color FillColor { get; set; } = Color.FromArgb(180, 236, 72, 153); // pink
    public GradientFill? Gradient { get; set; }
    public Color StrokeColor { get; set; } = Color.FromArgb(255, 219, 39, 119);
    public double StrokeThickness { get; set; } = 2;
    public StrokeDashStyle DashStyle { get; set; } = StrokeDashStyle.Solid;

    public EllipseElement()
    {
        Width = 100;
        Height = 100;
    }

    public override void Draw(DrawingContext dc)
    {
        IBrush fill = Gradient?.ToBrush() ?? new SolidColorBrush(FillColor);
        var pen = StrokeThickness > 0
            ? new Pen(new SolidColorBrush(StrokeColor), StrokeThickness, ShapeDashStyles.ToAvaloniaDashStyle(DashStyle))
            : null;

        var center = new Point(X + Width / 2, Y + Height / 2);
        dc.DrawEllipse(fill, pen, center, Width / 2, Height / 2);
    }

    public override bool HitTest(Point pt)
    {
        // Ellipse hit-test: normalized distance from center <= 1
        double cx = X + Width / 2;
        double cy = Y + Height / 2;
        double rx = Width / 2;
        double ry = Height / 2;
        if (rx <= 0 || ry <= 0) return false;
        double dx = (pt.X - cx) / rx;
        double dy = (pt.Y - cy) / ry;
        return (dx * dx + dy * dy) <= 1.0;
    }

    public override CanvasElement Clone() => new EllipseElement
    {
        X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        RotationDegrees = RotationDegrees, FlipX = FlipX, FlipY = FlipY,
        Opacity = Opacity, IsVisible = IsVisible, IsLocked = false, Name = Name, GroupId = null,
        AspectLocked = AspectLocked,
        FillColor = FillColor, Gradient = Gradient, StrokeColor = StrokeColor,
        StrokeThickness = StrokeThickness, DashStyle = DashStyle,
    };
}
