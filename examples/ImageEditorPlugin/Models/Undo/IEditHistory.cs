using System;

namespace PdfEditorApp.Plugins.ImageEditor.Models.Undo;

public interface IEditHistory
{
    bool CanUndo { get; }
    bool CanRedo { get; }

    /// <summary>Raised whenever <see cref="CanUndo"/>/<see cref="CanRedo"/> may have changed.</summary>
    event EventHandler? HistoryChanged;

    /// <summary>Records a mutation that has ALREADY been applied. Clears the redo stack.</summary>
    void Push(IUndoableCommand command);

    void Undo();
    void Redo();

    /// <summary>Discards all history without undoing anything (e.g. on ClearCanvas).</summary>
    void Clear();
}
