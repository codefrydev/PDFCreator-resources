using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>Implemented by every stroked shape (Rectangle, Ellipse, Arrow) — lets the
/// Properties panel bind generically without a converter per concrete shape type.</summary>
public interface IHasStroke
{
    Color StrokeColor { get; set; }
    double StrokeThickness { get; set; }
    StrokeDashStyle DashStyle { get; set; }
}
