using Avalonia;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

public enum GradientKind
{
    Linear,
    Radial,
}

public readonly record struct GradientStopValue(double Offset, Color Color);

/// <summary>
/// Immutable gradient-fill description for RectangleElement/EllipseElement — additive
/// alongside FillColor (null Gradient = "use FillColor as a solid fill"). Every edit swaps the
/// whole object, so the existing GenericPropertyCommand&lt;TTarget,TValue&gt; already covers undo.
/// </summary>
public sealed class GradientFill
{
    public GradientKind Kind { get; init; } = GradientKind.Linear;

    /// <summary>Direction of the gradient for Linear fills; ignored for Radial.</summary>
    public double AngleDegrees { get; init; }

    public IReadOnlyList<GradientStopValue> Stops { get; init; } = Array.Empty<GradientStopValue>();

    public IBrush ToBrush()
    {
        var stops = new GradientStops();
        foreach (var s in Stops) stops.Add(new GradientStop(s.Color, s.Offset));

        if (Kind == GradientKind.Radial)
        {
            return new RadialGradientBrush
            {
                GradientStops = stops,
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            };
        }

        double theta = AngleDegrees * Math.PI / 180.0;
        var dir = new Point(Math.Cos(theta), Math.Sin(theta));
        return new LinearGradientBrush
        {
            GradientStops = stops,
            StartPoint = new RelativePoint(0.5 - dir.X * 0.5, 0.5 - dir.Y * 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5 + dir.X * 0.5, 0.5 + dir.Y * 0.5, RelativeUnit.Relative),
        };
    }

    public static GradientFill CreateDefault(GradientKind kind, Color from, Color to) => new()
    {
        Kind = kind,
        AngleDegrees = 45,
        Stops = [new GradientStopValue(0, from), new GradientStopValue(1, to)],
    };
}
