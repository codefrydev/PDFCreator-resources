using Avalonia.Media;
using PdfEditorApp.Plugins.ImageEditor.Utils;

namespace PdfEditorApp.Plugins.ImageEditor.Models.TextStyles;

/// <summary>Immutable snapshot of every TextElement property a style preset touches — used both
/// to apply a preset (TextStyleDefinition.ApplyTo) and to capture "before"/"after" state for
/// undo (see ApplyTextStyle in ImageEditorViewModel). Holds the resolved FontFamily itself
/// rather than a family-name string, so restoring "before" never needs to re-derive a name from
/// an already-constructed FontFamily.</summary>
public readonly record struct TextStyleSnapshot(
    FontFamily FontFamily,
    double FontSize,
    FontWeight FontWeight,
    FontStyle FontStyle,
    Color ForegroundColor,
    Color? BackgroundColor,
    bool IsUnderline,
    bool IsStrikethrough,
    TextCaseTransform TextCaseTransform)
{
    public void ApplyTo(TextElement target)
    {
        target.FontFamily = FontFamily;
        target.FontSize = FontSize;
        target.FontWeight = FontWeight;
        target.FontStyle = FontStyle;
        target.ForegroundColor = ForegroundColor;
        target.BackgroundColor = BackgroundColor;
        target.IsUnderline = IsUnderline;
        target.IsStrikethrough = IsStrikethrough;
        target.TextCaseTransform = TextCaseTransform;
    }

    public static TextStyleSnapshot CaptureFrom(TextElement el) => new(
        el.FontFamily, el.FontSize, el.FontWeight, el.FontStyle, el.ForegroundColor,
        el.BackgroundColor, el.IsUnderline, el.IsStrikethrough, el.TextCaseTransform);
}

/// <summary>A named, curated combination of TextElement style properties (font/size/weight/
/// color/case/etc.) the user can apply to selected text in one click. Mirrors
/// Templates/TemplateDefinition.cs's plain-data-record pattern, but a style mutates the
/// selected element in place rather than building fresh canvas elements — see
/// Models/Templates/TemplateLibrary.cs's own doc comment for why the factory/plain-data split
/// matters (independently-mutable instances per use, no shared state across applications).</summary>
public sealed class TextStyleDefinition
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string FontFamilyName { get; init; }
    public double FontSize { get; init; } = 24;
    public FontWeight FontWeight { get; init; } = FontWeight.Normal;
    public FontStyle FontStyle { get; init; } = FontStyle.Normal;
    public Color ForegroundColor { get; init; } = Colors.Black;
    public Color? BackgroundColor { get; init; }
    public bool IsUnderline { get; init; }
    public bool IsStrikethrough { get; init; }
    public TextCaseTransform TextCaseTransform { get; init; } = TextCaseTransform.None;

    /// <summary>Resolved once per binding access (FontHelper memoizes internally, so repeated
    /// access is cheap) — lets the Text Styles flyout render each preset's name in its own
    /// face, the same live-preview trick the Typefaces flyout uses.</summary>
    public FontFamily PreviewFamily => FontHelper.CreateFontFamily(FontFamilyName);

    public void ApplyTo(TextElement target) => ToSnapshot().ApplyTo(target);

    private TextStyleSnapshot ToSnapshot() => new(
        FontHelper.CreateFontFamily(FontFamilyName), FontSize, FontWeight, FontStyle,
        ForegroundColor, BackgroundColor, IsUnderline, IsStrikethrough, TextCaseTransform);
}
