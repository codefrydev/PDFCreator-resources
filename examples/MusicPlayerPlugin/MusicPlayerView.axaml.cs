using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace PdfEditorApp.Plugins.MusicPlayer;

public partial class MusicPlayerView : UserControl
{
    private MusicPlayerViewModel? _viewModel;
    private bool _isDraggingSeek;

    public MusicPlayerView()
    {
        InitializeComponent();

        // Attach with handledEventsToo: true so the internal Slider Thumb does not swallow pointer events
        SeekSlider.AddHandler(InputElement.PointerPressedEvent, SeekSlider_OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        SeekSlider.AddHandler(InputElement.PointerReleasedEvent, SeekSlider_OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        SeekSlider.AddHandler(InputElement.PointerCaptureLostEvent, SeekSlider_OnPointerCaptureLost, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.AddFilesRequested -= OnAddFilesRequested;
        }

        _viewModel = DataContext as MusicPlayerViewModel;

        if (_viewModel is not null)
        {
            _viewModel.AddFilesRequested += OnAddFilesRequested;
        }
    }

    private async void OnAddFilesRequested(object? sender, EventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Audio Files (MP3, WAV, FLAC)",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio Files (*.mp3, *.wav, *.flac)")
                    {
                        Patterns = new[] { "*.mp3", "*.wav", "*.flac" }
                    },
                    new FilePickerFileType("All Files (*.*)")
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            });

            if (files.Count == 0) return;

            var paths = files
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();

            if (_viewModel is not null && paths.Count > 0)
            {
                await _viewModel.AddTracksAsync(paths);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MusicPlayerView] File picker error: {ex.Message}");
        }
    }

    private void SeekSlider_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isDraggingSeek = true;
        _viewModel?.BeginSeek();
    }

    private void SeekSlider_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingSeek)
        {
            _isDraggingSeek = false;
            _viewModel?.CommitSeek(SeekSlider.Value);
        }
    }

    private void SeekSlider_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDraggingSeek)
        {
            _isDraggingSeek = false;
            _viewModel?.CommitSeek(SeekSlider.Value);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Space && _viewModel is not null)
        {
            _ = _viewModel.PlayPauseAsync();
            e.Handled = true;
        }
    }
}
