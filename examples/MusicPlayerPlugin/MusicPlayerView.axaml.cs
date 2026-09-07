using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace PdfEditorApp.Plugins.MusicPlayer;

public partial class MusicPlayerView : UserControl
{
    private MusicPlayerViewModel? _viewModel;
    private bool _isDraggingSeek;
    private bool _wasInMiniMode;
    private double _restoredWindowHeight = 580;
    private double _restoredWindowWidth = 380;

    public MusicPlayerView()
    {
        InitializeComponent();

        AttachSeekHandlers(SeekSlider);
        AttachSeekHandlers(MiniSeekSlider);

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
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MusicPlayerViewModel;

        if (_viewModel is not null)
        {
            _viewModel.AddFilesRequested += OnAddFilesRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateHostWindowSize();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MusicPlayerViewModel.CurrentViewMode))
        {
            UpdateHostWindowSize();
        }
    }

    private void UpdateHostWindowSize()
    {
        if (_viewModel is null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        // Only dynamically adjust size when hosted in the standalone Runner window.
        // Never mutate the host document studio window of FryPDF!
        if (topLevel is Window window && window.Title?.Contains("Music Player") == true)
        {
            if (_viewModel.IsMiniMode)
            {
                if (!_wasInMiniMode)
                {
                    if (window.Height >= 300) _restoredWindowHeight = window.Height;
                    if (window.Width >= 300) _restoredWindowWidth = window.Width;
                    _wasInMiniMode = true;
                }
                window.Height = 110;
            }
            else
            {
                if (_wasInMiniMode)
                {
                    _wasInMiniMode = false;
                    window.Height = _restoredWindowHeight >= 400 ? _restoredWindowHeight : 580;
                    window.Width = _restoredWindowWidth >= 340 ? _restoredWindowWidth : 380;
                }
            }
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

    public void OnTrackCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Visual);
        if (!point.Properties.IsLeftButtonPressed) return;

        // If the click originated from an interactive button (Favorite, Remove, Play button), let that button handle it
        Visual? visual = e.Source as Visual;
        while (visual is not null && visual != sender)
        {
            if (visual is Button) return;
            visual = visual.GetVisualParent();
        }

        if (sender is Control control && control.DataContext is TrackViewModel track)
        {
            var vm = _viewModel ?? (DataContext as MusicPlayerViewModel);
            if (vm is not null)
            {
                _ = vm.PlayTrackAsync(track);
                e.Handled = true;
            }
        }
    }

    private void AttachSeekHandlers(Slider? slider)
    {
        if (slider is null) return;
        slider.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
        {
            _isDraggingSeek = true;
            _viewModel?.BeginSeek();
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        slider.AddHandler(InputElement.PointerReleasedEvent, (s, e) =>
        {
            if (_isDraggingSeek)
            {
                _isDraggingSeek = false;
                _viewModel?.CommitSeek(slider.Value);
            }
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        slider.AddHandler(InputElement.PointerCaptureLostEvent, (s, e) =>
        {
            if (_isDraggingSeek)
            {
                _isDraggingSeek = false;
                _viewModel?.CommitSeek(slider.Value);
            }
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
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
