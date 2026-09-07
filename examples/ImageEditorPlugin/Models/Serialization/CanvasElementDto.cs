using System;
using System.Text.Json.Serialization;

namespace PdfEditorApp.Plugins.ImageEditor.Models.Serialization;

/// <summary>
/// Separate DTO hierarchy for project save/load — kept independent of the runtime
/// CanvasElement model (rather than annotating it directly) to avoid attribute-collision
/// risk as the runtime model keeps evolving. Colors/enums are stored as strings so the JSON
/// is human-diffable; <see cref="Models.Serialization.CanvasElementMapper"/> converts both ways.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RectangleElementDto), "rectangle")]
[JsonDerivedType(typeof(EllipseElementDto), "ellipse")]
[JsonDerivedType(typeof(ArrowElementDto), "arrow")]
[JsonDerivedType(typeof(TextElementDto), "text")]
[JsonDerivedType(typeof(ImageElementDto), "image")]
public abstract class CanvasElementDto
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int ZIndex { get; set; }
    public double RotationDegrees { get; set; }
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    public double Opacity { get; set; } = 1.0;
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public string? Name { get; set; }
    public Guid? GroupId { get; set; }
    public bool AspectLocked { get; set; }
}

public sealed class GradientStopDto
{
    public double Offset { get; set; }
    public string Color { get; set; } = "#FF000000";
}

public sealed class GradientFillDto
{
    public string Kind { get; set; } = "Linear";
    public double AngleDegrees { get; set; }
    public System.Collections.Generic.List<GradientStopDto> Stops { get; set; } = new();
}

public sealed class RectangleElementDto : CanvasElementDto
{
    public string FillColor { get; set; } = "#FFFFFFFF";
    public GradientFillDto? Gradient { get; set; }
    public string StrokeColor { get; set; } = "#FF000000";
    public double StrokeThickness { get; set; }
    public string DashStyle { get; set; } = "Solid";
    public double CornerRadius { get; set; }
}

public sealed class EllipseElementDto : CanvasElementDto
{
    public string FillColor { get; set; } = "#FFFFFFFF";
    public GradientFillDto? Gradient { get; set; }
    public string StrokeColor { get; set; } = "#FF000000";
    public double StrokeThickness { get; set; }
    public string DashStyle { get; set; } = "Solid";
}

public sealed class ArrowElementDto : CanvasElementDto
{
    public string StrokeColor { get; set; } = "#FF000000";
    public double StrokeThickness { get; set; }
    public string DashStyle { get; set; } = "Solid";
    public double ArrowHeadSize { get; set; } = 12;
}

public sealed class TextElementDto : CanvasElementDto
{
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Inter,Segoe UI,Arial,sans-serif";
    public double FontSize { get; set; } = 24;
    public string FontWeight { get; set; } = "Normal";
    public string FontStyle { get; set; } = "Normal";
    public string ForegroundColor { get; set; } = "#FF000000";
    public string? BackgroundColor { get; set; }
    public string Alignment { get; set; } = "Left";
    public bool IsUnderline { get; set; }
    public bool IsStrikethrough { get; set; }
    public string TextCaseTransform { get; set; } = "None";
    public bool IsWidthAutoFitEnabled { get; set; } = true;
    public bool IsHeightAutoFitEnabled { get; set; } = true;
    public bool IsWrapEnabled { get; set; }
    public double LineHeight { get; set; }
}

public sealed class ImageElementDto : CanvasElementDto
{
    public string? FilePath { get; set; }
    public int NaturalPixelWidth { get; set; }
    public int NaturalPixelHeight { get; set; }
    public double? CropX { get; set; }
    public double? CropY { get; set; }
    public double? CropWidth { get; set; }
    public double? CropHeight { get; set; }
    public double Brightness { get; set; }
    public double Contrast { get; set; }
    public double Saturation { get; set; }
    public double Hue { get; set; }
    public double Temperature { get; set; }
    public double Tint { get; set; }
    public string? ActiveFilterPreset { get; set; }
    public double BlurRadius { get; set; }
    public double SharpenAmount { get; set; }

    /// <summary>The original imported file's raw encoded bytes, Base64-encoded — this is what
    /// makes a .fryimg file self-contained (no dependency on the original file still existing
    /// on disk at the recorded FilePath).</summary>
    public string? ImageBytesBase64 { get; set; }
}

public sealed class ProjectFileDto
{
    public int SchemaVersion { get; set; } = 1;
    public double CanvasWidth { get; set; } = 640;
    public double CanvasHeight { get; set; } = 480;
    public string BackgroundColor { get; set; } = "#FFFFFFFF";
    public System.Collections.Generic.List<CanvasElementDto> Elements { get; set; } = new();
}
