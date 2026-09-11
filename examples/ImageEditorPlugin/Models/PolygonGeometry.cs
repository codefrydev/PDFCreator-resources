using Avalonia;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>Shared vertex-array geometry helpers for polygon-based shapes (PolygonElement,
/// StarElement) — building a closed figure and testing point-containment work identically for
/// any simple polygon, convex (a regular polygon) or not (a star's vertices are non-convex),
/// so this is genuine shared logic rather than a premature abstraction over a couple of lines.</summary>
internal static class PolygonGeometry
{
    public static StreamGeometry BuildClosedFigure(Point[] vertices)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(vertices[0], isFilled: true);
            for (int i = 1; i < vertices.Length; i++) ctx.LineTo(vertices[i]);
            ctx.EndFigure(isClosed: true);
        }
        return geometry;
    }

    /// <summary>Standard ray-casting point-in-polygon test — correct for both convex (regular
    /// polygon) and non-convex (star) vertex sets, unlike a simple bounding/radius check.</summary>
    public static bool Contains(Point[] vertices, Point pt)
    {
        bool inside = false;
        int n = vertices.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var vi = vertices[i];
            var vj = vertices[j];
            bool crosses = vi.Y > pt.Y != vj.Y > pt.Y &&
                pt.X < (vj.X - vi.X) * (pt.Y - vi.Y) / (vj.Y - vi.Y) + vi.X;
            if (crosses) inside = !inside;
        }
        return inside;
    }
}
