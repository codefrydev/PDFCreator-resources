using System.Collections.Generic;

namespace PdfEditorApp.Plugins.ImageEditor.Models.Stickers;

/// <summary>Built-in sticker presets, grouped by Category. Mirrors ShapeLibrary/
/// TemplateLibrary's single static All list of factory entries.</summary>
public static class StickerLibrary
{
    public static IReadOnlyList<StickerDefinition> All { get; } =
    [
        // ── Faces ──
        new() { Glyph = "😀", Name = "Grinning", Category = "Faces" },
        new() { Glyph = "😍", Name = "Heart Eyes", Category = "Faces" },
        new() { Glyph = "😎", Name = "Cool", Category = "Faces" },
        new() { Glyph = "🥳", Name = "Party Face", Category = "Faces" },
        new() { Glyph = "😂", Name = "Laughing", Category = "Faces" },
        new() { Glyph = "😮", Name = "Surprised", Category = "Faces" },

        // ── Hearts & Symbols ──
        new() { Glyph = "❤️", Name = "Heart", Category = "Hearts & Symbols" },
        new() { Glyph = "⭐", Name = "Star", Category = "Hearts & Symbols" },
        new() { Glyph = "✨", Name = "Sparkles", Category = "Hearts & Symbols" },
        new() { Glyph = "🔥", Name = "Fire", Category = "Hearts & Symbols" },
        new() { Glyph = "💯", Name = "100", Category = "Hearts & Symbols" },
        new() { Glyph = "✅", Name = "Check", Category = "Hearts & Symbols" },

        // ── Nature & Weather ──
        new() { Glyph = "🌟", Name = "Glowing Star", Category = "Nature & Weather" },
        new() { Glyph = "☀️", Name = "Sun", Category = "Nature & Weather" },
        new() { Glyph = "🌈", Name = "Rainbow", Category = "Nature & Weather" },
        new() { Glyph = "🍃", Name = "Leaf", Category = "Nature & Weather" },

        // ── Celebration & Objects ──
        new() { Glyph = "🎉", Name = "Party Popper", Category = "Celebration & Objects" },
        new() { Glyph = "🎁", Name = "Gift", Category = "Celebration & Objects" },
        new() { Glyph = "📸", Name = "Camera", Category = "Celebration & Objects" },
        new() { Glyph = "💡", Name = "Idea", Category = "Celebration & Objects" },
        new() { Glyph = "🚀", Name = "Rocket", Category = "Celebration & Objects" },
        new() { Glyph = "🏆", Name = "Trophy", Category = "Celebration & Objects" },
    ];
}
