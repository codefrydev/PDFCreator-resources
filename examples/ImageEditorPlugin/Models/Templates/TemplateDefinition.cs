using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models.Templates;

/// <summary>
/// A starter template — buildable entirely from existing element types (no new asset
/// dependency). BuildElements is a factory (not a fixed list) so every "Use Template" click
/// gets fresh, independently-mutable element instances rather than sharing state across uses.
/// </summary>
public sealed class TemplateDefinition
{
    public required string Name { get; init; }
    public required double CanvasWidth { get; init; }
    public required double CanvasHeight { get; init; }
    public required Color BackgroundColor { get; init; }
    public string Category { get; init; } = "General";
    public required Func<List<CanvasElement>> BuildElements { get; init; }
}
