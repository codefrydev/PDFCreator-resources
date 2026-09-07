using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;

namespace PdfEditorApp.Plugins.ImageEditor.Models.Undo;

public sealed class AddElementCommand : IUndoableCommand
{
    private readonly ObservableCollection<CanvasElement> _elements;
    private readonly CanvasElement _element;

    public AddElementCommand(ObservableCollection<CanvasElement> elements, CanvasElement element)
    {
        _elements = elements;
        _element = element;
    }

    public string Description => $"Add {_element.ElementType}";
    public void Undo() => _elements.Remove(_element);
    public void Redo()
    {
        if (!_elements.Contains(_element)) _elements.Add(_element);
    }
}

public sealed class RemoveElementsCommand : IUndoableCommand
{
    private readonly ObservableCollection<CanvasElement> _elements;
    private readonly List<(CanvasElement Element, int Index)> _removed;

    public RemoveElementsCommand(ObservableCollection<CanvasElement> elements, IEnumerable<CanvasElement> toRemove)
    {
        _elements = elements;
        _removed = toRemove.Select(e => (e, elements.IndexOf(e))).OrderBy(t => t.Item2).ToList();
    }

    public string Description => _removed.Count == 1
        ? $"Delete {_removed[0].Element.ElementType}"
        : $"Delete {_removed.Count} elements";

    public void Undo()
    {
        foreach (var (element, index) in _removed)
        {
            int insertAt = Math.Min(index, _elements.Count);
            if (insertAt < 0) insertAt = _elements.Count;
            _elements.Insert(insertAt, element);
        }
    }

    public void Redo()
    {
        foreach (var (element, _) in _removed) _elements.Remove(element);
    }
}

public sealed class MoveElementsCommand : IUndoableCommand
{
    private readonly IReadOnlyList<(CanvasElement Element, Point OldPos, Point NewPos)> _moves;

    public MoveElementsCommand(IReadOnlyList<(CanvasElement Element, Point OldPos, Point NewPos)> moves)
    {
        _moves = moves;
    }

    public string Description => _moves.Count == 1 ? "Move element" : $"Move {_moves.Count} elements";

    public void Undo()
    {
        foreach (var (el, oldPos, _) in _moves) { el.X = oldPos.X; el.Y = oldPos.Y; }
    }

    public void Redo()
    {
        foreach (var (el, _, newPos) in _moves) { el.X = newPos.X; el.Y = newPos.Y; }
    }
}

public sealed class ResizeElementCommand : IUndoableCommand
{
    private readonly CanvasElement _element;
    private readonly Rect _oldBounds;
    private readonly Rect _newBounds;

    public ResizeElementCommand(CanvasElement element, Rect oldBounds, Rect newBounds)
    {
        _element = element;
        _oldBounds = oldBounds;
        _newBounds = newBounds;
    }

    public string Description => $"Resize {_element.ElementType}";
    public void Undo() => Apply(_oldBounds);
    public void Redo() => Apply(_newBounds);

    private void Apply(Rect r)
    {
        _element.X = r.X;
        _element.Y = r.Y;
        _element.Width = r.Width;
        _element.Height = r.Height;
    }
}

public sealed class ZOrderShiftCommand : IUndoableCommand
{
    private readonly IReadOnlyDictionary<CanvasElement, int> _before;
    private readonly IReadOnlyDictionary<CanvasElement, int> _after;

    public ZOrderShiftCommand(IReadOnlyDictionary<CanvasElement, int> before, IReadOnlyDictionary<CanvasElement, int> after)
    {
        _before = before;
        _after = after;
    }

    public string Description => "Reorder layers";
    public void Undo() { foreach (var kv in _before) kv.Key.ZIndex = kv.Value; }
    public void Redo() { foreach (var kv in _after) kv.Key.ZIndex = kv.Value; }
}

public sealed class ClearCanvasCommand : IUndoableCommand
{
    private readonly ObservableCollection<CanvasElement> _elements;
    private readonly List<CanvasElement> _snapshot;

    public ClearCanvasCommand(ObservableCollection<CanvasElement> elements, IEnumerable<CanvasElement> snapshot)
    {
        _elements = elements;
        _snapshot = snapshot.ToList();
    }

    public string Description => "Clear canvas";
    public void Undo()
    {
        foreach (var el in _snapshot)
        {
            if (!_elements.Contains(el)) _elements.Add(el);
        }
    }
    public void Redo() => _elements.Clear();
}

public sealed class BackgroundChangeCommand : IUndoableCommand
{
    private readonly Action<string> _applyByName;
    private readonly string _oldName;
    private readonly string _newName;

    public BackgroundChangeCommand(Action<string> applyByName, string oldName, string newName)
    {
        _applyByName = applyByName;
        _oldName = oldName;
        _newName = newName;
    }

    public string Description => "Change background";
    public void Undo() => _applyByName(_oldName);
    public void Redo() => _applyByName(_newName);
}

/// <summary>
/// Generic hook for simple property swaps (e.g. rotation, flip, crop rect, filter
/// adjustments) that other parts of the plugin push without needing their own
/// bespoke <see cref="IUndoableCommand"/> type. Must only be used for plain-value
/// properties — never for anything that owns an unmanaged/disposable resource.
/// </summary>
public sealed class GenericPropertyCommand<TTarget, TValue> : IUndoableCommand
{
    private readonly TTarget _target;
    private readonly Action<TTarget, TValue> _setter;
    private readonly TValue _oldValue;
    private readonly TValue _newValue;

    public GenericPropertyCommand(TTarget target, Action<TTarget, TValue> setter, TValue oldValue, TValue newValue, string description)
    {
        _target = target;
        _setter = setter;
        _oldValue = oldValue;
        _newValue = newValue;
        Description = description;
    }

    public string Description { get; }
    public void Undo() => _setter(_target, _oldValue);
    public void Redo() => _setter(_target, _newValue);
}

/// <summary>
/// Generalizes <see cref="GenericPropertyCommand{TTarget,TValue}"/> to N targets in one step —
/// e.g. "apply this fill color to every selected element" as a single undo entry.
/// </summary>
public sealed class BatchPropertyCommand<TTarget, TValue> : IUndoableCommand
{
    private readonly IReadOnlyList<(TTarget Target, TValue OldValue, TValue NewValue)> _changes;
    private readonly Action<TTarget, TValue> _setter;

    public BatchPropertyCommand(
        IReadOnlyList<(TTarget Target, TValue OldValue, TValue NewValue)> changes,
        Action<TTarget, TValue> setter,
        string description)
    {
        _changes = changes;
        _setter = setter;
        Description = description;
    }

    public string Description { get; }
    public void Undo() { foreach (var (target, oldValue, _) in _changes) _setter(target, oldValue); }
    public void Redo() { foreach (var (target, _, newValue) in _changes) _setter(target, newValue); }
}

/// <summary>Adds several new elements as one undo step (Duplicate, Paste).</summary>
public sealed class AddMultipleElementsCommand : IUndoableCommand
{
    private readonly ObservableCollection<CanvasElement> _elements;
    private readonly IReadOnlyList<CanvasElement> _added;

    public AddMultipleElementsCommand(ObservableCollection<CanvasElement> elements, IReadOnlyList<CanvasElement> added)
    {
        _elements = elements;
        _added = added;
    }

    public string Description => _added.Count == 1 ? $"Add {_added[0].ElementType}" : $"Add {_added.Count} elements";
    public void Undo() { foreach (var el in _added) _elements.Remove(el); }
    public void Redo() { foreach (var el in _added) { if (!_elements.Contains(el)) _elements.Add(el); } }
}

/// <summary>
/// In-place image replace — bespoke rather than <see cref="GenericPropertyCommand{TTarget,TValue}"/>
/// because it owns disposable Bitmap objects and must dispose the currently-inactive one only
/// after the swap completes, never both eagerly.
/// </summary>
public sealed class ReplaceImageCommand : IUndoableCommand
{
    private readonly ImageElement _element;
    private readonly byte[]? _oldBytes;
    private readonly int _oldNaturalWidth, _oldNaturalHeight;
    private readonly byte[]? _newBytes;
    private readonly int _newNaturalWidth, _newNaturalHeight;
    private readonly Action<ImageElement, byte[]?, int, int> _applyAndDecode;

    public ReplaceImageCommand(
        ImageElement element,
        byte[]? oldBytes, int oldNaturalWidth, int oldNaturalHeight,
        byte[]? newBytes, int newNaturalWidth, int newNaturalHeight,
        Action<ImageElement, byte[]?, int, int> applyAndDecode)
    {
        _element = element;
        _oldBytes = oldBytes; _oldNaturalWidth = oldNaturalWidth; _oldNaturalHeight = oldNaturalHeight;
        _newBytes = newBytes; _newNaturalWidth = newNaturalWidth; _newNaturalHeight = newNaturalHeight;
        _applyAndDecode = applyAndDecode;
    }

    public string Description => "Replace image";
    public void Undo() => _applyAndDecode(_element, _oldBytes, _oldNaturalWidth, _oldNaturalHeight);
    public void Redo() => _applyAndDecode(_element, _newBytes, _newNaturalWidth, _newNaturalHeight);
}

/// <summary>Rotates N elements' position (about a shared pivot) and own rotation together as one step.</summary>
public sealed class GroupRotateCommand : IUndoableCommand
{
    private readonly IReadOnlyDictionary<CanvasElement, (double X, double Y, double Rotation)> _before;
    private readonly IReadOnlyDictionary<CanvasElement, (double X, double Y, double Rotation)> _after;

    public GroupRotateCommand(
        IReadOnlyDictionary<CanvasElement, (double X, double Y, double Rotation)> before,
        IReadOnlyDictionary<CanvasElement, (double X, double Y, double Rotation)> after)
    {
        _before = before;
        _after = after;
    }

    public string Description => $"Rotate {_after.Count} elements";
    public void Undo() { foreach (var kv in _before) Apply(kv.Key, kv.Value); }
    public void Redo() { foreach (var kv in _after) Apply(kv.Key, kv.Value); }
    private static void Apply(CanvasElement el, (double X, double Y, double Rotation) v) { el.X = v.X; el.Y = v.Y; el.RotationDegrees = v.Rotation; }
}

/// <summary>Assigns/clears GroupId across a selection as one step — used for both Group and Ungroup.</summary>
public sealed class GroupingChangeCommand : IUndoableCommand
{
    private readonly IReadOnlyDictionary<CanvasElement, Guid?> _before;
    private readonly IReadOnlyDictionary<CanvasElement, Guid?> _after;

    public GroupingChangeCommand(IReadOnlyDictionary<CanvasElement, Guid?> before, IReadOnlyDictionary<CanvasElement, Guid?> after)
    {
        _before = before;
        _after = after;
    }

    public string Description => "Group/ungroup elements";
    public void Undo() { foreach (var kv in _before) kv.Key.GroupId = kv.Value; }
    public void Redo() { foreach (var kv in _after) kv.Key.GroupId = kv.Value; }
}

/// <summary>Proportionally resizes N elements together (as one group-resize gesture) as one step.</summary>
public sealed class GroupResizeCommand : IUndoableCommand
{
    private readonly IReadOnlyList<(CanvasElement Element, Rect Old, Rect New)> _changes;

    public GroupResizeCommand(IReadOnlyList<(CanvasElement Element, Rect Old, Rect New)> changes)
    {
        _changes = changes;
    }

    public string Description => $"Resize {_changes.Count} elements";
    public void Undo() { foreach (var (el, old, _) in _changes) Apply(el, old); }
    public void Redo() { foreach (var (el, _, @new) in _changes) Apply(el, @new); }
    private static void Apply(CanvasElement el, Rect r) { el.X = r.X; el.Y = r.Y; el.Width = r.Width; el.Height = r.Height; }
}

/// <summary>Undoable canvas width/height change.</summary>
public sealed class CanvasResizeCommand : IUndoableCommand
{
    private readonly Action<double, double> _apply;
    private readonly double _oldW, _oldH, _newW, _newH;

    public CanvasResizeCommand(Action<double, double> apply, double oldW, double oldH, double newW, double newH)
    {
        _apply = apply;
        _oldW = oldW; _oldH = oldH; _newW = newW; _newH = newH;
    }

    public string Description => "Resize canvas";
    public void Undo() => _apply(_oldW, _oldH);
    public void Redo() => _apply(_newW, _newH);
}
