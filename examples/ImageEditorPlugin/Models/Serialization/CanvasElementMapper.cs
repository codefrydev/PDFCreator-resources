using System;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PdfEditorApp.Plugins.ImageEditor.Models.Serialization;

/// <summary>Converts between the runtime <see cref="CanvasElement"/> model and its DTO
/// counterpart for project save/load.</summary>
public static class CanvasElementMapper
{
    public static CanvasElementDto ToDto(CanvasElement element) => element switch
    {
        RectangleElement r => ToRectangleDto(r),
        EllipseElement e => ToEllipseDto(e),
        ArrowElement a => ToArrowDto(a),
        TextElement t => ToTextDto(t),
        ImageElement img => ToImageDto(img),
        _ => throw new NotSupportedException($"No DTO mapping for element type '{element.GetType().Name}'."),
    };

    public static CanvasElement FromDto(CanvasElementDto dto) => dto switch
    {
        RectangleElementDto r => FromRectangleDto(r),
        EllipseElementDto e => FromEllipseDto(e),
        ArrowElementDto a => FromArrowDto(a),
        TextElementDto t => FromTextDto(t),
        ImageElementDto img => FromImageDto(img),
        _ => throw new NotSupportedException($"No element mapping for DTO type '{dto.GetType().Name}'."),
    };

    private static void CopyBaseToDto(CanvasElement el, CanvasElementDto dto)
    {
        dto.X = el.X; dto.Y = el.Y; dto.Width = el.Width; dto.Height = el.Height;
        dto.ZIndex = el.ZIndex;
        dto.RotationDegrees = el.RotationDegrees; dto.FlipX = el.FlipX; dto.FlipY = el.FlipY;
        dto.Opacity = el.Opacity; dto.IsVisible = el.IsVisible; dto.IsLocked = el.IsLocked;
        dto.Name = el.Name; dto.GroupId = el.GroupId; dto.AspectLocked = el.AspectLocked;
    }

    private static void CopyBaseFromDto(CanvasElementDto dto, CanvasElement el)
    {
        el.X = dto.X; el.Y = dto.Y; el.Width = dto.Width; el.Height = dto.Height;
        el.ZIndex = dto.ZIndex;
        el.RotationDegrees = dto.RotationDegrees; el.FlipX = dto.FlipX; el.FlipY = dto.FlipY;
        el.Opacity = dto.Opacity; el.IsVisible = dto.IsVisible; el.IsLocked = dto.IsLocked;
        el.Name = dto.Name; el.GroupId = dto.GroupId; el.AspectLocked = dto.AspectLocked;
    }

    private static GradientFillDto? ToGradientDto(GradientFill? gradient)
    {
        if (gradient == null) return null;
        var dto = new GradientFillDto { Kind = gradient.Kind.ToString(), AngleDegrees = gradient.AngleDegrees };
        foreach (var stop in gradient.Stops)
        {
            dto.Stops.Add(new GradientStopDto { Offset = stop.Offset, Color = stop.Color.ToString() });
        }
        return dto;
    }

    private static GradientFill? FromGradientDto(GradientFillDto? dto)
    {
        if (dto == null) return null;
        var stops = new GradientStopValue[dto.Stops.Count];
        for (int i = 0; i < dto.Stops.Count; i++)
        {
            stops[i] = new GradientStopValue(dto.Stops[i].Offset, Color.Parse(dto.Stops[i].Color));
        }
        return new GradientFill
        {
            Kind = Enum.Parse<GradientKind>(dto.Kind),
            AngleDegrees = dto.AngleDegrees,
            Stops = stops,
        };
    }

    private static RectangleElementDto ToRectangleDto(RectangleElement r)
    {
        var dto = new RectangleElementDto
        {
            FillColor = r.FillColor.ToString(),
            Gradient = ToGradientDto(r.Gradient),
            StrokeColor = r.StrokeColor.ToString(),
            StrokeThickness = r.StrokeThickness,
            DashStyle = r.DashStyle.ToString(),
            CornerRadius = r.CornerRadius,
        };
        CopyBaseToDto(r, dto);
        return dto;
    }

    private static RectangleElement FromRectangleDto(RectangleElementDto dto)
    {
        var r = new RectangleElement
        {
            FillColor = Color.Parse(dto.FillColor),
            Gradient = FromGradientDto(dto.Gradient),
            StrokeColor = Color.Parse(dto.StrokeColor),
            StrokeThickness = dto.StrokeThickness,
            DashStyle = Enum.Parse<StrokeDashStyle>(dto.DashStyle),
            CornerRadius = dto.CornerRadius,
        };
        CopyBaseFromDto(dto, r);
        return r;
    }

    private static EllipseElementDto ToEllipseDto(EllipseElement e)
    {
        var dto = new EllipseElementDto
        {
            FillColor = e.FillColor.ToString(),
            Gradient = ToGradientDto(e.Gradient),
            StrokeColor = e.StrokeColor.ToString(),
            StrokeThickness = e.StrokeThickness,
            DashStyle = e.DashStyle.ToString(),
        };
        CopyBaseToDto(e, dto);
        return dto;
    }

    private static EllipseElement FromEllipseDto(EllipseElementDto dto)
    {
        var e = new EllipseElement
        {
            FillColor = Color.Parse(dto.FillColor),
            Gradient = FromGradientDto(dto.Gradient),
            StrokeColor = Color.Parse(dto.StrokeColor),
            StrokeThickness = dto.StrokeThickness,
            DashStyle = Enum.Parse<StrokeDashStyle>(dto.DashStyle),
        };
        CopyBaseFromDto(dto, e);
        return e;
    }

    private static ArrowElementDto ToArrowDto(ArrowElement a)
    {
        var dto = new ArrowElementDto
        {
            StrokeColor = a.StrokeColor.ToString(),
            StrokeThickness = a.StrokeThickness,
            DashStyle = a.DashStyle.ToString(),
            ArrowHeadSize = a.ArrowHeadSize,
        };
        CopyBaseToDto(a, dto);
        return dto;
    }

    private static ArrowElement FromArrowDto(ArrowElementDto dto)
    {
        var a = new ArrowElement
        {
            StrokeColor = Color.Parse(dto.StrokeColor),
            StrokeThickness = dto.StrokeThickness,
            DashStyle = Enum.Parse<StrokeDashStyle>(dto.DashStyle),
            ArrowHeadSize = dto.ArrowHeadSize,
        };
        CopyBaseFromDto(dto, a);
        return a;
    }

    private static TextElementDto ToTextDto(TextElement t)
    {
        var dto = new TextElementDto
        {
            Text = t.Text,
            FontFamily = t.FontFamily.Name,
            FontSize = t.FontSize,
            FontWeight = t.FontWeight.ToString(),
            FontStyle = t.FontStyle.ToString(),
            ForegroundColor = t.ForegroundColor.ToString(),
            BackgroundColor = t.BackgroundColor?.ToString(),
            Alignment = t.Alignment.ToString(),
            IsUnderline = t.IsUnderline,
            IsStrikethrough = t.IsStrikethrough,
            TextCaseTransform = t.TextCaseTransform.ToString(),
            IsWidthAutoFitEnabled = t.IsWidthAutoFitEnabled,
            IsHeightAutoFitEnabled = t.IsHeightAutoFitEnabled,
            IsWrapEnabled = t.IsWrapEnabled,
            LineHeight = t.LineHeight,
        };
        CopyBaseToDto(t, dto);
        return dto;
    }

    private static TextElement FromTextDto(TextElementDto dto)
    {
        // Guard auto-fit off first so setting Text/FontSize/etc. below (which normally trigger
        // a remeasure) don't clobber the dimensions restored by CopyBaseFromDto — mirrors
        // TextElement.Clone()'s same construction-order guard.
        var t = new TextElement { IsWidthAutoFitEnabled = false, IsHeightAutoFitEnabled = false };
        t.FontFamily = new FontFamily(dto.FontFamily);
        t.Text = dto.Text;
        t.FontSize = dto.FontSize;
        t.FontWeight = Enum.Parse<FontWeight>(dto.FontWeight);
        t.FontStyle = Enum.Parse<FontStyle>(dto.FontStyle);
        t.ForegroundColor = Color.Parse(dto.ForegroundColor);
        t.BackgroundColor = dto.BackgroundColor != null ? Color.Parse(dto.BackgroundColor) : null;
        t.Alignment = Enum.Parse<TextAlignment>(dto.Alignment);
        t.IsUnderline = dto.IsUnderline;
        t.IsStrikethrough = dto.IsStrikethrough;
        t.TextCaseTransform = Enum.Parse<TextCaseTransform>(dto.TextCaseTransform);
        t.IsWrapEnabled = dto.IsWrapEnabled;
        t.LineHeight = dto.LineHeight;

        CopyBaseFromDto(dto, t);

        // Restore the REAL auto-fit flags only now that every text-affecting property is set.
        t.IsWidthAutoFitEnabled = dto.IsWidthAutoFitEnabled;
        t.IsHeightAutoFitEnabled = dto.IsHeightAutoFitEnabled;
        return t;
    }

    private static ImageElementDto ToImageDto(ImageElement img)
    {
        var dto = new ImageElementDto
        {
            FilePath = img.FilePath,
            NaturalPixelWidth = img.NaturalPixelWidth,
            NaturalPixelHeight = img.NaturalPixelHeight,
            CropX = img.SourceCropRect?.X,
            CropY = img.SourceCropRect?.Y,
            CropWidth = img.SourceCropRect?.Width,
            CropHeight = img.SourceCropRect?.Height,
            Brightness = img.Brightness,
            Contrast = img.Contrast,
            Saturation = img.Saturation,
            Hue = img.Hue,
            Temperature = img.Temperature,
            Tint = img.Tint,
            ActiveFilterPreset = img.ActiveFilterPreset,
            BlurRadius = img.BlurRadius,
            SharpenAmount = img.SharpenAmount,
            ImageBytesBase64 = img.OriginalImageBytes is { Length: > 0 } bytes ? Convert.ToBase64String(bytes) : null,
        };
        CopyBaseToDto(img, dto);
        return dto;
    }

    private static ImageElement FromImageDto(ImageElementDto dto)
    {
        var img = new ImageElement
        {
            FilePath = dto.FilePath,
            NaturalPixelWidth = dto.NaturalPixelWidth,
            NaturalPixelHeight = dto.NaturalPixelHeight,
            SourceCropRect = dto.CropX is { } cx && dto.CropY is { } cy && dto.CropWidth is { } cw && dto.CropHeight is { } ch
                ? new Rect(cx, cy, cw, ch)
                : null,
            Brightness = dto.Brightness,
            Contrast = dto.Contrast,
            Saturation = dto.Saturation,
            Hue = dto.Hue,
            Temperature = dto.Temperature,
            Tint = dto.Tint,
            ActiveFilterPreset = dto.ActiveFilterPreset,
            BlurRadius = dto.BlurRadius,
            SharpenAmount = dto.SharpenAmount,
        };

        if (!string.IsNullOrEmpty(dto.ImageBytesBase64))
        {
            var bytes = Convert.FromBase64String(dto.ImageBytesBase64);
            img.OriginalImageBytes = bytes;
            using var ms = new MemoryStream(bytes);
            img.Source = new Bitmap(ms);
        }

        CopyBaseFromDto(dto, img);
        return img;
    }
}
