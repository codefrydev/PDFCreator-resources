using System.Collections.Generic;

namespace PdfEditorApp.Plugins.ImageEditor.Models.Shapes;

/// <summary>Built-in shape presets, grouped by Category — every entry is built entirely from
/// existing element types (Rectangle/Ellipse/Polygon/Star/Arrow), so this needs no new asset
/// dependency, following Templates/TemplateLibrary.cs's own stated design goal for the same
/// reason. A single static All list of factory calls, mirroring TemplateLibrary exactly.</summary>
public static class ShapeLibrary
{
    public static IReadOnlyList<ShapeDefinition> All { get; } =
    [
        // ── Basic ──
        new() { Name = "Rectangle", Category = "Basic", Build = () => new RectangleElement() },
        new() { Name = "Rounded Square", Category = "Basic", Build = () => new RectangleElement { Width = 100, Height = 100, CornerRadius = 24 } },
        new() { Name = "Circle", Category = "Basic", Build = () => new EllipseElement() },
        new() { Name = "Triangle", Category = "Basic", Build = () => new PolygonElement { SideCount = 3 } },
        new() { Name = "Pentagon", Category = "Basic", Build = () => new PolygonElement { SideCount = 5 } },
        new() { Name = "Hexagon", Category = "Basic", Build = () => new PolygonElement { SideCount = 6 } },
        new() { Name = "Octagon", Category = "Basic", Build = () => new PolygonElement { SideCount = 8 } },

        // ── Stars & Bursts ──
        new() { Name = "Star", Category = "Stars & Bursts", Build = () => new StarElement { PointCount = 5 } },
        new() { Name = "Sharp Star", Category = "Stars & Bursts", Build = () => new StarElement { PointCount = 5, InnerRadiusRatio = 0.35 } },
        new() { Name = "Six-Point Star", Category = "Stars & Bursts", Build = () => new StarElement { PointCount = 6, InnerRadiusRatio = 0.55 } },
        new() { Name = "Burst", Category = "Stars & Bursts", Build = () => new StarElement { PointCount = 12, InnerRadiusRatio = 0.75 } },

        // ── Arrows & Lines ──
        new() { Name = "Arrow", Category = "Arrows & Lines", Build = () => new ArrowElement() },
        new() { Name = "Line", Category = "Arrows & Lines", Build = () => new ArrowElement { ArrowHeadSize = 0 } },
        new() { Name = "Thick Line", Category = "Arrows & Lines", Build = () => new ArrowElement { ArrowHeadSize = 0, StrokeThickness = 6 } },
        new() { Name = "Dashed Line", Category = "Arrows & Lines", Build = () => new ArrowElement { ArrowHeadSize = 0, DashStyle = StrokeDashStyle.Dashed } },
    ];
}
