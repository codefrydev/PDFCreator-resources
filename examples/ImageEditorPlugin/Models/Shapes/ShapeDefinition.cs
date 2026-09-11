using System;

namespace PdfEditorApp.Plugins.ImageEditor.Models.Shapes;

/// <summary>A library entry that builds one fresh CanvasElement — mirrors
/// Templates/TemplateDefinition.cs's factory-delegate pattern (fresh, independently-mutable
/// instance per use) but produces a single element to insert into the existing canvas rather
/// than a whole new canvas to replace it with.</summary>
public sealed class ShapeDefinition
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required Func<CanvasElement> Build { get; init; }
}
