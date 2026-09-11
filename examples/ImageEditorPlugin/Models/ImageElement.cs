using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>
/// An imported bitmap image placed on the canvas.
/// </summary>
public class ImageElement : CanvasElement
{
    public override string ElementType => "Image";

    public Bitmap? Source { get; set; }
    public string? FilePath { get; set; }

    /// <summary>Pixel dimensions of the original imported bitmap (source-space, unaffected
    /// by any crop or on-canvas resize).</summary>
    public int NaturalPixelWidth { get; set; }
    public int NaturalPixelHeight { get; set; }

    /// <summary>The visible sub-rect of the source image, in source-pixel space. Null means
    /// the full image is shown.</summary>
    public Rect? SourceCropRect { get; set; }

    public Rect EffectiveSourceRect =>
        SourceCropRect ?? new Rect(0, 0, NaturalPixelWidth, NaturalPixelHeight);

    /// <summary>Aspect ratio of the currently-visible (post-crop) content.</summary>
    public double CurrentAspectRatio =>
        EffectiveSourceRect.Height > 0 ? EffectiveSourceRect.Width / EffectiveSourceRect.Height : 1.0;

    // ── Color adjustments (brightness/contrast/saturation/hue/temperature/tint/filter presets) ──
    public double Brightness { get; set; }        // -100..100, default 0
    public double Contrast { get; set; }           // -100..100, default 0
    public double Saturation { get; set; }         // -100..100, default 0
    public double Hue { get; set; }                // -180..180, default 0
    public double Temperature { get; set; }        // -100..100, default 0
    public double Tint { get; set; }               // -100..100, default 0
    public string? ActiveFilterPreset { get; set; } // null | "BlackAndWhite" | "Sepia" | "Vintage" | "Vivid" | "Noir" | "Invert" | "Duotone" | "Vignette"

    // ── Spatial adjustments (blur/sharpen) ──────────────────────────────────────────────
    public double BlurRadius { get; set; }         // 0..~20, default 0
    public double SharpenAmount { get; set; }      // 0..~2, default 0

    /// <summary>Raw encoded bytes of the originally-imported file — the source adjustments
    /// are always recomputed from, so brightness/contrast/saturation never compound.</summary>
    public byte[]? OriginalImageBytes { get; set; }

    /// <summary>Cached bitmap with the current adjustments/filter baked in. Null means
    /// "use <see cref="Source"/> unadjusted".</summary>
    public Bitmap? AdjustedSource { get; set; }

    /// <summary>Race-guard for out-of-order async adjustment recomputation — not serialized.</summary>
    public int AdjustmentVersion;

    /// <summary>Transient "before" preview toggle for the Adjust panel — when true, Draw()
    /// shows the original unedited image regardless of AdjustedSource. Never serialized/cloned;
    /// the ViewModel owns clearing it when the preview is dismissed or selection changes.</summary>
    public bool PreviewOriginal { get; set; }

    public ImageElement()
    {
        Width = 200;
        Height = 150;
        AspectLocked = true; // images default to locked; shapes (base default) do not
    }

    public override double GetAspectRatio() => CurrentAspectRatio;

    public override void Draw(DrawingContext dc)
    {
        var toDraw = PreviewOriginal ? Source : (AdjustedSource ?? Source);
        if (toDraw is null)
        {
            // Draw placeholder if no image loaded
            var placeholderBrush = new SolidColorBrush(Color.FromArgb(60, 200, 200, 200));
            var placeholderPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 180, 180, 180)), 1.5);
            dc.DrawRectangle(placeholderBrush, placeholderPen, new Rect(X, Y, Width, Height), 4, 4);

            // Draw camera icon text placeholder
            var typeface = new Typeface("Arial");
            var ft = new FormattedText(
                "📷",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                24,
                new SolidColorBrush(Color.FromArgb(160, 200, 200, 200)));
            dc.DrawText(ft, new Point(X + Width / 2 - 12, Y + Height / 2 - 12));
            return;
        }

        // Two-rect draw: samples EffectiveSourceRect (what part of the source is visible,
        // e.g. after a crop) into the element's on-canvas frame — decoupled from stretch,
        // which is what actually fixes the old aspect-distortion-on-resize bug.
        var sourceRect = NaturalPixelWidth > 0 && NaturalPixelHeight > 0
            ? EffectiveSourceRect
            : new Rect(0, 0, toDraw.PixelSize.Width, toDraw.PixelSize.Height);
        dc.DrawImage(toDraw, sourceRect, new Rect(X, Y, Width, Height));

        if (!PreviewOriginal && ActiveFilterPreset == "Vignette")
        {
            DrawVignetteOverlay(dc);
        }
    }

    private void DrawVignetteOverlay(DrawingContext dc)
    {
        var rect = new Rect(X, Y, Width, Height);
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0.0),
                new GradientStop(Colors.Transparent, 0.55),
                new GradientStop(Color.FromArgb(140, 0, 0, 0), 1.0),
            }
        };
        dc.DrawRectangle(brush, null, rect);
    }

    public override CanvasElement Clone()
    {
        var clone = new ImageElement
        {
            X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
            RotationDegrees = RotationDegrees, FlipX = FlipX, FlipY = FlipY,
            Opacity = Opacity, IsVisible = IsVisible, IsLocked = false, Name = Name, GroupId = null,
            AspectLocked = AspectLocked,
            FilePath = FilePath,
            NaturalPixelWidth = NaturalPixelWidth, NaturalPixelHeight = NaturalPixelHeight,
            SourceCropRect = SourceCropRect,
            Brightness = Brightness, Contrast = Contrast, Saturation = Saturation,
            Hue = Hue, Temperature = Temperature, Tint = Tint,
            ActiveFilterPreset = ActiveFilterPreset,
            BlurRadius = BlurRadius, SharpenAmount = SharpenAmount,
            OriginalImageBytes = OriginalImageBytes, // shared by reference — never mutated in place
        };

        if (OriginalImageBytes is { Length: > 0 } bytes)
        {
            using var ms = new System.IO.MemoryStream(bytes);
            clone.Source = new Bitmap(ms);
        }
        // AdjustedSource intentionally starts null — the caller (ImageEditorViewModel) is
        // responsible for re-triggering the adjustment bake if any adjustment is non-default.

        return clone;
    }
}
