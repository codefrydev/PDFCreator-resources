using System.Collections.Generic;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models.TextStyles;

/// <summary>Built-in text style presets, grouped by Category. Every preset uses only the
/// "Basics" font group ImageEditorViewModel.BuildFontGroups always has available (no package
/// download needed), so applying one never has to wait on a background fetch. Mirrors
/// Templates/TemplateLibrary.cs: a single static All list of plain data records.</summary>
public static class TextStyleLibrary
{
    public static IReadOnlyList<TextStyleDefinition> All { get; } =
    [
        // ── Headlines ──
        new()
        {
            Name = "Bold Impact", Category = "Headlines", FontFamilyName = "Oswald",
            FontSize = 48, FontWeight = FontWeight.Bold, TextCaseTransform = TextCaseTransform.Uppercase,
        },
        new()
        {
            Name = "Modern Headline", Category = "Headlines", FontFamilyName = "Montserrat",
            FontSize = 40, FontWeight = FontWeight.Bold,
        },
        new()
        {
            Name = "Elegant Serif", Category = "Headlines", FontFamilyName = "Playfair Display",
            FontSize = 44, FontWeight = FontWeight.Normal,
        },
        new()
        {
            Name = "Condensed Poster", Category = "Headlines", FontFamilyName = "Bebas Neue",
            FontSize = 52, TextCaseTransform = TextCaseTransform.Uppercase,
        },

        // ── Body & Captions ──
        new()
        {
            Name = "Clean Body", Category = "Body & Captions", FontFamilyName = "Inter",
            FontSize = 18, FontWeight = FontWeight.Normal,
        },
        new()
        {
            Name = "Readable Serif", Category = "Body & Captions", FontFamilyName = "Lora",
            FontSize = 18,
        },
        new()
        {
            Name = "Soft Caption", Category = "Body & Captions", FontFamilyName = "Noto Sans",
            FontSize = 14, ForegroundColor = Color.FromArgb(255, 90, 90, 90),
        },

        // ── Script & Display ──
        new()
        {
            Name = "Signature Script", Category = "Script & Display", FontFamilyName = "Great Vibes",
            FontSize = 42,
        },
        new()
        {
            Name = "Playful Script", Category = "Script & Display", FontFamilyName = "Dancing Script",
            FontSize = 36, FontWeight = FontWeight.Bold,
        },
        new()
        {
            Name = "Retro Display", Category = "Script & Display", FontFamilyName = "Lobster",
            FontSize = 38,
        },
        new()
        {
            Name = "Fun Comic", Category = "Script & Display", FontFamilyName = "Comic Neue",
            FontSize = 28, FontWeight = FontWeight.Bold,
        },

        // ── Bold Statements ──
        new()
        {
            Name = "Neon Tech", Category = "Bold Statements", FontFamilyName = "Orbitron",
            FontSize = 32, ForegroundColor = Color.FromArgb(255, 34, 211, 238), TextCaseTransform = TextCaseTransform.Uppercase,
        },
        new()
        {
            Name = "Classic Stamp", Category = "Bold Statements", FontFamilyName = "Cinzel",
            FontSize = 30, TextCaseTransform = TextCaseTransform.Uppercase,
        },
        new()
        {
            Name = "Highlighted Quote", Category = "Bold Statements", FontFamilyName = "Merriweather",
            FontSize = 26, FontStyle = FontStyle.Italic, BackgroundColor = Color.FromArgb(90, 250, 204, 21),
        },
    ];
}
