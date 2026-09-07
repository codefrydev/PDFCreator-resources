using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>Implemented by shapes with a solid/gradient fill (Rectangle, Ellipse) — lets the
/// Properties panel bind generically without a converter per concrete shape type.</summary>
public interface IHasFill
{
    Color FillColor { get; set; }
    GradientFill? Gradient { get; set; }
}
