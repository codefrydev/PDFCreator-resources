namespace PdfEditorApp.Plugins.ImageEditor.Models.Stickers;

/// <summary>A sticker is a large-glyph TextElement (an emoji or symbol), not a bundled image
/// asset — reuses the existing TextElement rendering path already proven with emoji (see
/// ImageEditorViewModel.AddSampleElements' "Image Editor 🎨" sample) instead of requiring new
/// binary assets and an avares:// packaging step. Width/Height are left on TextElement's own
/// auto-fit (the default), so the box always hugs the glyph tightly at FontSize regardless of
/// platform-specific emoji metrics.</summary>
public sealed class StickerDefinition
{
    public required string Glyph { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }

    public TextElement Build() => new()
    {
        Text = Glyph,
        FontSize = 64,
    };
}
