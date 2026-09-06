using System;
using System.Collections.Generic;
using System.IO;
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

        // Native Drag-and-Drop audio files and folders support (Avalonia 12)
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Formats.Contains(DataFormat.File)) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;

        var paths = new List<string>();
        var validExtensions = MusicPlayerViewModel.SupportedExtensions;

        foreach (var item in files)
        {
            var p = ResolveLocalPath(item);
            if (string.IsNullOrEmpty(p)) continue;

            if (File.Exists(p))
            {
                if (validExtensions.Contains(Path.GetExtension(p)))
                {
                    paths.Add(p);
                }
            }
            else if (Directory.Exists(p))
            {
                try
                {
                    var opt = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    };
                    foreach (var f in Directory.EnumerateFiles(p, "*.*", opt))
                    {
                        if (validExtensions.Contains(Path.GetExtension(f)))
                        {
                            paths.Add(f);
                        }
                    }
                }
                catch { }
            }
        }

        if (_viewModel is not null && paths.Count > 0)
        {
            await _viewModel.AddTracksAsync(paths);
        }
    }

    private IStorageProvider? GetStorageProvider()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not null)
        {
            return topLevel.StorageProvider;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is not null)
            {
                var mainTopLevel = TopLevel.GetTopLevel(desktop.MainWindow);
                if (mainTopLevel?.StorageProvider is not null)
                {
                    return mainTopLevel.StorageProvider;
                }
                if (desktop.MainWindow.StorageProvider is not null)
                {
                    return desktop.MainWindow.StorageProvider;
                }
            }

            foreach (var win in desktop.Windows)
            {
                var winTopLevel = TopLevel.GetTopLevel(win);
                if (winTopLevel?.StorageProvider is not null)
                {
                    return winTopLevel.StorageProvider;
                }
                if (win.StorageProvider is not null)
                {
                    return win.StorageProvider;
                }
            }
        }

        return null;
    }

    private static string? ResolveLocalPath(IStorageItem item)
    {
        var p = item.TryGetLocalPath();
        if (string.IsNullOrEmpty(p) && item.Path != null)
        {
            p = item.Path.IsFile ? item.Path.LocalPath : item.Path.AbsolutePath;
        }

        if (!string.IsNullOrEmpty(p))
        {
            p = Uri.UnescapeDataString(p);
            if (p.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(p, UriKind.Absolute, out var uri) && uri.IsFile)
                {
                    p = uri.LocalPath;
                }
            }
        }

        return p;
    }

    private async void OnAddFilesRequested(object? sender, EventArgs e)
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            if (_viewModel is not null)
            {
                _viewModel.StatusMessage = "Storage provider is not available.";
            }
            System.Diagnostics.Debug.WriteLine("[MusicPlayerView] StorageProvider could not be resolved.");
            return;
        }

        try
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Audio Files",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Supported Audio Files (*.mp3, *.wav, *.flac)")
                    {
                        Patterns = new[] { "*.mp3", "*.wav", "*.flac" }
                    },
                    new FilePickerFileType("MP3 Audio (*.mp3)")
                    {
                        Patterns = new[] { "*.mp3" }
                    },
                    new FilePickerFileType("WAV Audio (*.wav)")
                    {
                        Patterns = new[] { "*.wav" }
                    },
                    new FilePickerFileType("FLAC Audio (*.flac)")
                    {
                        Patterns = new[] { "*.flac" }
                    },
                    FilePickerFileTypes.All
                }
            });

            if (files is null || files.Count == 0) return;

            var paths = new List<string>();
            foreach (var f in files)
            {
                var p = ResolveLocalPath(f);
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    paths.Add(p);
                }
            }

            if (_viewModel is not null && paths.Count > 0)
            {
                await _viewModel.AddTracksAsync(paths);
            }
        }
        catch (Exception ex)
        {
            if (_viewModel is not null)
            {
                _viewModel.StatusMessage = $"File picker error: {ex.Message}";
            }
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
