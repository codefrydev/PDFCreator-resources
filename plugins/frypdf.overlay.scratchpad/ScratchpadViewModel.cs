using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PdfEditorApp.Plugins.Scratchpad;

/// <summary>
/// ViewModel for the floating review scratchpad overlay.
/// Supports markdown note taking, word & character counts, and quick clipboard export.
/// </summary>
public partial class ScratchpadViewModel : ObservableObject
{
    private readonly IServiceProvider? _serviceProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WordCount))]
    [NotifyPropertyChangedFor(nameof(CharacterCount))]
    private string _notesText = "# PDF Review Notes\n- Checked page 1 header\n- Approved layout on page 3\n- Ready for signing";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public int CharacterCount => NotesText?.Length ?? 0;

    public int WordCount
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NotesText)) return 0;
            return NotesText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }

    public ScratchpadViewModel(IServiceProvider? serviceProvider = null)
    {
        _serviceProvider = serviceProvider;
    }

    [RelayCommand]
    public void ClearNotes()
    {
        NotesText = string.Empty;
        StatusMessage = "Cleared notes";
    }

    [RelayCommand]
    public void AddTimestamp()
    {
        var stamp = $"\n\n## Note ({DateTime.Now:HH:mm:ss})\n- ";
        NotesText += stamp;
        StatusMessage = "Added timestamp note";
    }
}
