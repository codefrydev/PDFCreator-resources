namespace PdfEditorApp.Plugins.ImageEditor.Models.Undo;

/// <summary>
/// A single reversible mutation already applied to the canvas. <see cref="Undo"/> and
/// <see cref="Redo"/> must never themselves push a new command onto history — callers
/// are expected to invoke them only through <see cref="IEditHistory"/>.
/// </summary>
public interface IUndoableCommand
{
    void Undo();
    void Redo();
    string Description { get; }
}
