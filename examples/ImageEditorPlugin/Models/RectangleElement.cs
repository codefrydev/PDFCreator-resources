using Avalonia;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>
/// A filled/stroked rectangle on the canvas with optional corner radius.
/// </summary>
public class RectangleElement : CanvasElement, IHasFill, IHasStroke
{
    public override string ElementType => "Rectangle";

    public Color FillColor { get; set; } = Color.FromArgb(180, 99, 102, 241); // indigo
    public GradientFill? Gradient { get; set; }
    public Color StrokeColor { get; set; } = Color.FromArgb(255, 79, 81, 200);
    public double StrokeThickness { get; set; } = 2;
    public StrokeDashStyle DashStyle { get; set; } = StrokeDashStyle.Solid;
    public double CornerRadius { get; set; } = 8;

    public RectangleElement()
    {
        Width = 120;
        Height = 80;
    }

    public override void Draw(DrawingContext dc)
    {
        IBrush fill = Gradient?.ToBrush() ?? new SolidColorBrush(FillColor);
        var pen = StrokeThickness > 0
            ? new Pen(new SolidColorBrush(StrokeColor), StrokeThickness, ShapeDashStyles.ToAvaloniaDashStyle(DashStyle))
            : null;

        var rect = new Rect(X, Y, Width, Height);
        dc.DrawRectangle(fill, pen, rect, CornerRadius, CornerRadius);
    }

    public override CanvasElement Clone() => new RectangleElement
    {
        X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        RotationDegrees = RotationDegrees, FlipX = FlipX, FlipY = FlipY,
        Opacity = Opacity, IsVisible = IsVisible, IsLocked = false, Name = Name, GroupId = null,
        AspectLocked = AspectLocked,
        FillColor = FillColor, Gradient = Gradient, StrokeColor = StrokeColor,
        StrokeThickness = StrokeThickness, DashStyle = DashStyle, CornerRadius = CornerRadius,
    };
}
