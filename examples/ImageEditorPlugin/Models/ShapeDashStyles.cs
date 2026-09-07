namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>Maps the model-level <see cref="StrokeDashStyle"/> enum to a real Avalonia
/// DashStyle for use in a Pen. A separate static helper (rather than a method on the model)
/// avoids a naming collision between the StrokeDashStyle-typed "DashStyle" property each
/// shape exposes and Avalonia's own Media.DashStyle type.</summary>
public static class ShapeDashStyles
{
    public static Avalonia.Media.DashStyle? ToAvaloniaDashStyle(StrokeDashStyle style) => style switch
    {
        StrokeDashStyle.Dashed => new Avalonia.Media.DashStyle(new double[] { 4, 3 }, 0),
        StrokeDashStyle.Dotted => new Avalonia.Media.DashStyle(new double[] { 1, 2 }, 0),
        _ => null,
    };
}
