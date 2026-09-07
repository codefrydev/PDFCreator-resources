using Avalonia;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>
/// Abstract base for all canvas elements in the Image Editor.
/// Each element knows how to draw itself via Avalonia DrawingContext.
/// </summary>
public abstract class CanvasElement
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Top-left X position on the canvas.</summary>
    public double X { get; set; }

    /// <summary>Top-left Y position on the canvas.</summary>
    public double Y { get; set; }

    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Render order — higher = drawn on top.</summary>
    public int ZIndex { get; set; }

    /// <summary>Clockwise rotation in degrees about the element's <see cref="Bounds"/> center.</summary>
    public double RotationDegrees { get; set; }

    /// <summary>Mirrors the element horizontally about its <see cref="Bounds"/> center, applied before rotation.</summary>
    public bool FlipX { get; set; }

    /// <summary>Mirrors the element vertically about its <see cref="Bounds"/> center, applied before rotation.</summary>
    public bool FlipY { get; set; }

    /// <summary>0 (fully transparent) .. 1 (fully opaque, default). Applied via dc.PushOpacity().</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>When false, the element is skipped by rendering, hit-testing, and marquee-select
    /// (but remains selectable/togglable from the Layers panel).</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>When true, the element cannot be selected via canvas click/marquee, moved,
    /// resized, or deleted (but remains selectable from the Layers panel to inspect/unlock).</summary>
    public bool IsLocked { get; set; }

    /// <summary>User-assigned layer name. Null falls back to <see cref="ElementType"/> via <see cref="DisplayName"/>.</summary>
    public string? Name { get; set; }

    /// <summary>Non-null Guid shared by every member of a persistent group; null means ungrouped.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>When true, resizing preserves <see cref="GetAspectRatio"/> unless the Shift
    /// modifier is held during the drag. Defaults false for shapes; ImageElement defaults true.</summary>
    public bool AspectLocked { get; set; }

    /// <summary>Display name shown in the layer panel.</summary>
    public abstract string ElementType { get; }

    /// <summary>The name shown in the Layers panel — <see cref="Name"/> if set, else <see cref="ElementType"/>.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? ElementType : Name!;

    /// <summary>Bounding rectangle for hit-testing and selection handles.</summary>
    public virtual Rect Bounds => new(X, Y, Width, Height);

    /// <summary>
    /// Bounding rectangle accounting for <see cref="RotationDegrees"/> — the true on-screen
    /// extent of a rotated element. Defaults to <see cref="Bounds"/> when not rotated.
    /// </summary>
    public virtual Rect GetRotatedBounds()
    {
        if (RotationDegrees == 0) return Bounds;

        var b = Bounds;
        var center = b.Center;
        double theta = RotationDegrees * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(theta));
        double sin = Math.Abs(Math.Sin(theta));
        double rotatedWidth = b.Width * cos + b.Height * sin;
        double rotatedHeight = b.Width * sin + b.Height * cos;
        return new Rect(center.X - rotatedWidth / 2, center.Y - rotatedHeight / 2, rotatedWidth, rotatedHeight);
    }

    /// <summary>Aspect ratio used by aspect-locked resize. Default reads current Width/Height;
    /// ImageElement overrides this to its crop-aware CurrentAspectRatio.</summary>
    public virtual double GetAspectRatio() => Width > 0 && Height > 0 ? Width / Height : 1.0;

    /// <summary>
    /// Draw this element. Called from EditorCanvasControl.Render().
    /// </summary>
    public abstract void Draw(DrawingContext dc);

    /// <summary>
    /// Returns true if the point (in canvas coordinates) is inside this element.
    /// Default uses bounding box; override for non-rectangular shapes.
    /// </summary>
    public virtual bool HitTest(Point pt) => Bounds.Contains(pt);

    /// <summary>
    /// Rotation/flip-aware hit test — transforms the click point into the element's own local
    /// (unrotated/unflipped) space via the inverse of the same matrix DrawElement uses to
    /// render it, then delegates to the ordinary per-type <see cref="HitTest"/>. Precise even
    /// at arbitrary rotation angles (axis-aligned Bounds.Contains was only correct by
    /// coincidence at 0/90/180/270).
    /// </summary>
    public bool HitTestRotationAware(Point canvasPt)
    {
        if (RotationDegrees == 0 && !FlipX && !FlipY) return HitTest(canvasPt);

        var center = Bounds.Center;
        double theta = RotationDegrees * Math.PI / 180.0;
        double sx = FlipX ? -1.0 : 1.0;
        double sy = FlipY ? -1.0 : 1.0;

        var forward = Matrix.CreateTranslation(-center.X, -center.Y)
                    * Matrix.CreateScale(sx, sy)
                    * Matrix.CreateTranslation(center.X, center.Y)
                    * Matrix.CreateRotation(theta, center);

        return forward.TryInvert(out var inverse) ? HitTest(canvasPt.Transform(inverse)) : HitTest(canvasPt);
    }

    /// <summary>Creates an independent copy of this element (new Id). Subclasses copy every
    /// public settable property except Id; ImageElement shares its immutable OriginalImageBytes
    /// by reference but always independently re-decodes its Bitmap objects.</summary>
    public abstract CanvasElement Clone();
}
