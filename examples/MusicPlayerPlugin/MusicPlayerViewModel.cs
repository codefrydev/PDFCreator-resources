using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PdfEditorApp.Core.Plugins.Settings;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace PdfEditorApp.Plugins.MusicPlayer;

public enum RepeatMode
{
    Off,
    RepeatAll,
    RepeatOne
}

public enum PlayerViewMode
{
    Player,
    Queue,
    Mini
}

/// <summary>
/// High-performance, zero-lag ViewModel for the Material Design 3 Expressive Music Player.
/// Features fully asynchronous, off-UI-thread audio loading/seeking, live animated equalizer,
/// clean queue management, and persisted preferences.
/// </summary>
public partial class MusicPlayerViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Audio extensions natively supported by SoundFlow MiniAudio engine (MP3, WAV, FLAC).
    /// </summary>
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac"
    };
    private const string PluginId = "frypdf.overlay.musicplayer";
    private const string SettingVolume = "Volume";
    private const string SettingRepeatMode = "RepeatMode";
    private const string SettingShuffle = "Shuffle";
    private const string SettingPlaylist = "Playlist";
    private const string SettingLastTrackIndex = "LastTrackIndex";
    private const string SettingViewMode = "ViewMode";
    private const string SettingFavorites = "Favorites";

    private static readonly AudioFormat PlaybackFormat = AudioFormat.Cd;

    private readonly IPluginSettingsStore? _settingsStore;
    private MiniAudioEngine? _engine;
    private AudioPlaybackDevice? _device;

    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _visualizerTimer;
    private readonly Random _random = new();
    private readonly HashSet<string> _favoritePaths = new(StringComparer.OrdinalIgnoreCase);

    private FileStream? _stream;
    private StreamDataProvider? _dataProvider;
    private SoundPlayer? _player;

    private bool _isSeeking;
    private int _isHandlingPlaybackEnded;
    private int _currentIndex = -1;
    private double _previousVolume = 80;
    private double _visualizerPhase;

    public ObservableCollection<TrackViewModel> Playlist { get; } = new();
    public ObservableCollection<TrackViewModel> FilteredPlaylist { get; } = new();

    /// <summary>Raised when the user clicks "Add Files"; the View presents the native file picker.</summary>
    public event EventHandler? AddFilesRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentTrack))]
    [NotifyPropertyChangedFor(nameof(CurrentTrackTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentTrackArtist))]
    [NotifyPropertyChangedFor(nameof(CurrentTrackAlbum))]
    [NotifyPropertyChangedFor(nameof(CurrentTrackExtension))]
    [NotifyPropertyChangedFor(nameof(UpNextTrack))]
    [NotifyPropertyChangedFor(nameof(UpNextDisplay))]
    [NotifyPropertyChangedFor(nameof(HasUpNextTrack))]
    [NotifyPropertyChangedFor(nameof(IsCurrentTrackFavorite))]
    [NotifyPropertyChangedFor(nameof(CurrentTrackFavoriteIconKind))]
    private TrackViewModel? _currentTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerMode))]
    [NotifyPropertyChangedFor(nameof(IsQueueMode))]
    [NotifyPropertyChangedFor(nameof(IsMiniMode))]
    [NotifyPropertyChangedFor(nameof(IsStandardChromeVisible))]
    private PlayerViewMode _currentViewMode = PlayerViewMode.Player;

    public bool IsPlayerMode => CurrentViewMode == PlayerViewMode.Player;
    public bool IsQueueMode => CurrentViewMode == PlayerViewMode.Queue;
    public bool IsMiniMode => CurrentViewMode == PlayerViewMode.Mini;
    public bool IsStandardChromeVisible => CurrentViewMode != PlayerViewMode.Mini;

    partial void OnCurrentViewModeChanged(PlayerViewMode value)
    {
        _settingsStore?.SetSetting(PluginId, SettingViewMode, value.ToString());
    }

    public TrackViewModel? UpNextTrack
    {
        get
        {
            if (Playlist.Count == 0 || _currentIndex < 0) return null;
            var nextIndex = _currentIndex + 1;
            if (nextIndex < Playlist.Count) return Playlist[nextIndex];
            if (RepeatMode == RepeatMode.RepeatAll && Playlist.Count > 1) return Playlist[0];
            return null;
        }
    }

    public bool HasUpNextTrack => UpNextTrack is not null;

    public string UpNextDisplay => UpNextTrack is not null
        ? $"{UpNextTrack.Title} • {UpNextTrack.Artist}"
        : "Queue complete";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseIconKind))]
    [NotifyPropertyChangedFor(nameof(PlayPauseTooltip))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isLoadingAudio;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumePercentDisplay))]
    private double _volumePercent = 80;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIconKind))]
    private bool _isMuted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionDisplay))]
    [NotifyPropertyChangedFor(nameof(ElapsedDisplay))]
    [NotifyPropertyChangedFor(nameof(RemainingDisplay))]
    private double _positionSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionDisplay))]
    [NotifyPropertyChangedFor(nameof(RemainingDisplay))]
    private double _durationSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatModeTooltip))]
    [NotifyPropertyChangedFor(nameof(IsRepeatActive))]
    private RepeatMode _repeatMode = RepeatMode.Off;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShuffleTooltip))]
    private bool _isShuffle;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoritesFilterTooltip))]
    private bool _filterFavoritesOnly;

    partial void OnFilterFavoritesOnlyChanged(bool value) => ApplySearchFilter();

    public bool IsCurrentTrackFavorite => CurrentTrack?.IsFavorite ?? false;
    public string CurrentTrackFavoriteIconKind => IsCurrentTrackFavorite ? "Heart" : "HeartOutline";
    public int FavoritesCount => Playlist.Count(t => t.IsFavorite);
    public bool HasFavorites => FavoritesCount > 0;
    public string FavoritesFilterTooltip => FilterFavoritesOnly ? "Showing Favorites Only (Click to show all)" : "Filter by Favorites";

    [ObservableProperty]
    private bool _isLoadingFiles;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    // --- Live 5-Bar Equalizer Visualizer Heights (in dips) ---
    [ObservableProperty] private double _bar1Height = 6;
    [ObservableProperty] private double _bar2Height = 10;
    [ObservableProperty] private double _bar3Height = 16;
    [ObservableProperty] private double _bar4Height = 11;
    [ObservableProperty] private double _bar5Height = 7;

    // --- Computed Presentation Properties ---
    public bool HasCurrentTrack => CurrentTrack is not null;
    public string CurrentTrackTitle => CurrentTrack?.Title ?? "No track selected";
    public string CurrentTrackArtist => CurrentTrack?.Artist ?? "Add music files to begin";
    public string CurrentTrackAlbum => CurrentTrack?.Album ?? string.Empty;
    public string CurrentTrackExtension => CurrentTrack?.FileExtension ?? "AUDIO";

    public string PlayPauseIconKind => IsPlaying ? "Pause" : "Play";
    public string PlayPauseTooltip => IsPlaying ? "Pause (Space)" : "Play (Space)";

    public bool IsPaused => HasCurrentTrack && !IsPlaying;
    public string StatusBadgeText => IsPlaying ? "PLAYING" : (HasCurrentTrack ? "PAUSED" : "IDLE");

    public bool IsRepeatActive => RepeatMode != RepeatMode.Off;
    public string RepeatModeTooltip => RepeatMode switch
    {
        RepeatMode.RepeatAll => "Repeat: All Tracks",
        RepeatMode.RepeatOne => "Repeat: Single Track",
        _ => "Repeat: Off"
    };

    public string ShuffleTooltip => IsShuffle ? "Shuffle: On" : "Shuffle: Off";

    public string VolumeIconKind => (IsMuted || VolumePercent <= 0) ? "VolumeOff" : "VolumeHigh";
    public string VolumePercentDisplay => $"{(int)Math.Round(IsMuted ? 0 : VolumePercent)}%";

    public bool HasTracks => Playlist.Count > 0;
    public bool IsEmpty => Playlist.Count == 0;
    public bool ShowSearchFilter => Playlist.Count >= 3;
    public bool HasFewTracks => Playlist.Count > 0 && Playlist.Count <= 2;

    public string PositionDisplay => $"{FormatTime(PositionSeconds)} / {FormatTime(DurationSeconds)}";
    public string ElapsedDisplay => FormatTime(PositionSeconds);
    public string RemainingDisplay => DurationSeconds > 0
        ? $"-{FormatTime(Math.Max(0, DurationSeconds - PositionSeconds))}"
        : "0:00";

    public string TotalDurationDisplay
    {
        get
        {
            var total = TimeSpan.FromSeconds(Playlist.Sum(t => t.Duration.TotalSeconds));
            return total.TotalHours >= 1
                ? $"{(int)total.TotalHours}h {total.Minutes}m"
                : $"{(int)total.TotalMinutes}m {total.Seconds:D2}s";
        }
    }

    public string QueueSummaryDisplay => Playlist.Count switch
    {
        0 => "Empty Queue",
        1 => $"1 track • {TotalDurationDisplay}",
        _ => $"{Playlist.Count} tracks • {TotalDurationDisplay}"
    };

    public MusicPlayerViewModel(IServiceProvider? serviceProvider = null)
    {
        _settingsStore = serviceProvider?.GetService<IPluginSettingsStore>();

        InitializeAudioEngine();

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += OnPositionTimerTick;
        _positionTimer.Start();

        _visualizerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _visualizerTimer.Tick += OnVisualizerTick;

        _ = LoadSettingsAsync();
    }

    private void InitializeAudioEngine()
    {
        try
        {
            MusicPlayerPlugin.EnsureNativeAudioLibrariesLoaded();
            _engine = new MiniAudioEngine(Array.Empty<MiniAudioBackend>());
            _device = _engine.InitializePlaybackDevice(null, PlaybackFormat, new MiniAudioDeviceConfig());
            _device.Start();
        }
        catch (Exception ex)
        {
            StatusMessage = "Audio engine initialization failed.";
            System.Diagnostics.Debug.WriteLine($"[MusicPlayer] Audio init error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SwitchToPlayerView() => CurrentViewMode = PlayerViewMode.Player;

    [RelayCommand]
    private void SwitchToQueueView() => CurrentViewMode = PlayerViewMode.Queue;

    [RelayCommand]
    private void SwitchToMiniView() => CurrentViewMode = PlayerViewMode.Mini;

    [RelayCommand]
    private void SetViewMode(string mode)
    {
        if (Enum.TryParse<PlayerViewMode>(mode, true, out var parsed))
        {
            CurrentViewMode = parsed;
        }
    }

    [RelayCommand]
    public void ToggleFavorite(TrackViewModel? track)
    {
        if (track is null) return;
        track.IsFavorite = !track.IsFavorite;
        if (track.IsFavorite)
        {
            _favoritePaths.Add(track.FilePath);
        }
        else
        {
            _favoritePaths.Remove(track.FilePath);
        }

        SaveFavorites();
        OnPropertyChanged(nameof(FavoritesCount));
        OnPropertyChanged(nameof(HasFavorites));
        if (track == CurrentTrack)
        {
            OnPropertyChanged(nameof(IsCurrentTrackFavorite));
            OnPropertyChanged(nameof(CurrentTrackFavoriteIconKind));
        }

        if (FilterFavoritesOnly)
        {
            ApplySearchFilter();
        }
    }

    [RelayCommand]
    public void ToggleCurrentTrackFavorite() => ToggleFavorite(CurrentTrack);

    [RelayCommand]
    public void ToggleFavoritesFilter() => FilterFavoritesOnly = !FilterFavoritesOnly;

    [RelayCommand]
    public void PlayNextInQueue(TrackViewModel? track)
    {
        if (track is null || !Playlist.Contains(track)) return;
        var oldIdx = Playlist.IndexOf(track);
        if (oldIdx == _currentIndex) return;

        Playlist.RemoveAt(oldIdx);
        var insertIdx = Math.Min(_currentIndex + 1, Playlist.Count);
        Playlist.Insert(insertIdx, track);
        RebuildTrackNumbers();
        ApplySearchFilter();
        SavePlaylist();
        NotifyCollectionProperties();
        StatusMessage = $"Next up: {track.Title}";
    }

    [RelayCommand]
    public void RevealInFileManager(TrackViewModel? track)
    {
        if (track is null || !File.Exists(track.FilePath)) return;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", $"-R \"{track.FilePath}\"");
            }
            else if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{track.FilePath}\"");
            }
            else if (OperatingSystem.IsLinux())
            {
                var dir = Path.GetDirectoryName(track.FilePath) ?? track.FilePath;
                System.Diagnostics.Process.Start("xdg-open", $"\"{dir}\"");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MusicPlayer] Reveal error: {ex.Message}");
        }
    }

    [RelayCommand]
    public void SkipBackward10()
    {
        var target = Math.Clamp(PositionSeconds - 10, 0, DurationSeconds);
        CommitSeek(target);
    }

    [RelayCommand]
    public void SkipForward10()
    {
        var target = Math.Clamp(PositionSeconds + 10, 0, DurationSeconds);
        CommitSeek(target);
    }

    [RelayCommand]
    public void SortByTitle()
    {
        var current = CurrentTrack;
        var sorted = Playlist.OrderBy(t => t.Title).ToList();
        Playlist.Clear();
        foreach (var t in sorted) Playlist.Add(t);
        if (current is not null) _currentIndex = Playlist.IndexOf(current);
        RebuildTrackNumbers();
        ApplySearchFilter();
        SavePlaylist();
    }

    [RelayCommand]
    public void SortByArtist()
    {
        var current = CurrentTrack;
        var sorted = Playlist.OrderBy(t => t.Artist).ThenBy(t => t.Title).ToList();
        Playlist.Clear();
        foreach (var t in sorted) Playlist.Add(t);
        if (current is not null) _currentIndex = Playlist.IndexOf(current);
        RebuildTrackNumbers();
        ApplySearchFilter();
        SavePlaylist();
    }

    [RelayCommand]
    private void AddFiles() => AddFilesRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Discovers audio tracks automatically from the system's default Music folders
    /// asynchronously on a background worker thread.
    /// </summary>
    [RelayCommand]
    public async Task ScanDefaultMusicFolderAsync()
    {
        if (IsLoadingFiles)
        {
            StatusMessage = "A file scan is already in progress...";
            return;
        }

        var candidateDirs = new List<string>();

        try
        {
            var myMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!string.IsNullOrEmpty(myMusic) && Directory.Exists(myMusic))
            {
                candidateDirs.Add(myMusic);
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                // Apple Music default media folder on macOS
                var macMediaMusic = Path.Combine(home, "Music", "Music", "Media.localized", "Music");
                if (Directory.Exists(macMediaMusic))
                {
                    candidateDirs.Insert(0, macMediaMusic); // Prioritize actual music library
                }

                var standardMusic = Path.Combine(home, "Music");
                if (Directory.Exists(standardMusic) && !candidateDirs.Contains(standardMusic))
                {
                    candidateDirs.Add(standardMusic);
                }

                var downloads = Path.Combine(home, "Downloads");
                if (Directory.Exists(downloads))
                {
                    candidateDirs.Add(downloads);
                }
            }
        }
        catch { }

        if (candidateDirs.Count == 0)
        {
            StatusMessage = "Default Music folder not found.";
            return;
        }

        IsLoadingFiles = true;
        StatusMessage = "Discovering tracks in Music library...";

        bool accessRestricted = false;

        try
        {
            var foundPaths = await Task.Run(() =>
            {
                var validExtensions = SupportedExtensions;
                var results = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var opt = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                foreach (var dir in candidateDirs)
                {
                    try
                    {
                        var files = Directory.EnumerateFiles(dir, "*.*", opt);
                        foreach (var file in files)
                        {
                            if (validExtensions.Contains(Path.GetExtension(file)) && seen.Add(file))
                            {
                                results.Add(file);
                                if (results.Count >= 300) // Cap to first 300 tracks for fast ingestion
                                {
                                    return results;
                                }
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        accessRestricted = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MusicPlayer] Directory scan warning for '{dir}': {ex.Message}");
                    }
                }

                return results;
            });

            if (foundPaths.Count > 0)
            {
                await AddTracksAsync(foundPaths);
                StatusMessage = $"Added {foundPaths.Count} track{(foundPaths.Count == 1 ? "" : "s")} from Music library.";
            }
            else if (accessRestricted)
            {
                StatusMessage = "Music access restricted by macOS permissions. Check System Settings > Privacy > Files & Folders, or click 'Add Files'.";
            }
            else
            {
                StatusMessage = "No audio tracks found in default Music library.";
            }
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = "Music access restricted by macOS permissions. Check System Settings > Privacy > Files & Folders, or click 'Add Files'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[MusicPlayer] Scan error: {ex.Message}");
        }
        finally
        {
            IsLoadingFiles = false;
        }
    }

    /// <summary>
    /// Parses selected audio files in the background without freezing the UI event loop.
    /// </summary>
    public async Task AddTracksAsync(IEnumerable<string> filePaths)
    {
        var pathsList = filePaths
            .Where(f => File.Exists(f) && SupportedExtensions.Contains(Path.GetExtension(f)))
            .ToList();
        if (pathsList.Count == 0)
        {
            StatusMessage = "No supported audio files found (MP3, WAV, FLAC).";
            return;
        }

        IsLoadingFiles = true;
        StatusMessage = "Scanning audio files...";

        try
        {
            var existingSet = new HashSet<string>(Playlist.Select(p => p.FilePath), StringComparer.OrdinalIgnoreCase);
            int startingIndex = Playlist.Count + 1;

            var newTracks = await Task.Run(() =>
            {
                var loaded = new List<TrackViewModel>();
                int nextIndex = startingIndex;

                foreach (var path in pathsList)
                {
                    if (existingSet.Contains(path)) continue;
                    existingSet.Add(path);

                    var track = TrackViewModel.FromFile(path, nextIndex++);
                    loaded.Add(track);
                }

                return loaded;
            });

            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current == null)
            {
                ApplyNewTracks(newTracks);
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ApplyNewTracks(newTracks));
            }

            try
            {
                SavePlaylist();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MusicPlayer] SavePlaylist error: {ex.Message}");
            }

            StatusMessage = $"Loaded {Playlist.Count} track{(Playlist.Count == 1 ? "" : "s")}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error scanning audio files: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[MusicPlayer] AddTracks error: {ex.Message}");
        }
        finally
        {
            IsLoadingFiles = false;
        }
    }

    private void ApplyNewTracks(List<TrackViewModel> newTracks)
    {
        foreach (var track in newTracks)
        {
            track.IsFavorite = _favoritePaths.Contains(track.FilePath);
            Playlist.Add(track);
        }

        RebuildTrackNumbers();
        ApplySearchFilter();
        NotifyCollectionProperties();

        if (CurrentTrack is null && Playlist.Count > 0)
        {
            _currentIndex = 0;
            CurrentTrack = Playlist[0];
            DurationSeconds = CurrentTrack.Duration.TotalSeconds;
        }
    }


    [RelayCommand]
    public void RemoveTrack(TrackViewModel track)
    {
        var index = Playlist.IndexOf(track);
        if (index < 0) return;

        if (ReferenceEquals(track, CurrentTrack))
        {
            Stop();
            DisposeCurrentPlayer();
            CurrentTrack = null;
            DurationSeconds = 0;
            PositionSeconds = 0;
            _currentIndex = -1;
        }
        else if (index < _currentIndex)
        {
            _currentIndex--;
        }

        Playlist.Remove(track);
        RebuildTrackNumbers();
        ApplySearchFilter();
        NotifyCollectionProperties();
        SavePlaylist();
        SaveLastTrack();
    }

    [RelayCommand]
    public void ClearPlaylist()
    {
        Stop();
        DisposeCurrentPlayer();
        CurrentTrack = null;
        DurationSeconds = 0;
        PositionSeconds = 0;
        _currentIndex = -1;

        Playlist.Clear();
        FilteredPlaylist.Clear();
        NotifyCollectionProperties();
        SavePlaylist();
        SaveLastTrack();
        StatusMessage = "Queue cleared.";
    }

    [RelayCommand]
    public void ShufflePlaylist()
    {
        if (Playlist.Count <= 1) return;

        var current = CurrentTrack;
        var shuffled = Playlist.OrderBy(_ => _random.Next()).ToList();

        Playlist.Clear();
        foreach (var t in shuffled)
        {
            Playlist.Add(t);
        }

        RebuildTrackNumbers();
        ApplySearchFilter();

        if (current is not null)
        {
            _currentIndex = Playlist.IndexOf(current);
        }

        SavePlaylist();
        SaveLastTrack();
        StatusMessage = "Queue shuffled.";
    }

    /// <summary>
    /// Starts playback of the given track asynchronously on a worker thread to ensure ZERO UI freezing.
    /// </summary>
    [RelayCommand]
    public async Task PlayTrackAsync(TrackViewModel track)
    {
        var index = Playlist.IndexOf(track);
        if (index < 0) return;

        if (_device is null || _engine is null)
        {
            StatusMessage = "Audio device unavailable.";
            return;
        }

        if (!SupportedExtensions.Contains(Path.GetExtension(track.FilePath)))
        {
            StatusMessage = $"Cannot play '{track.Title}': only MP3, WAV, and FLAC are supported.";
            return;
        }

        IsLoadingAudio = true;
        _currentIndex = index;
        CurrentTrack = track;
        DurationSeconds = track.Duration.TotalSeconds;
        PositionSeconds = 0;

        try
        {
            await Task.Run(() =>
            {
                DisposeCurrentPlayer();

                if (!File.Exists(track.FilePath))
                {
                    throw new FileNotFoundException("Track file not found.", track.FilePath);
                }

                var stream = File.OpenRead(track.FilePath);
                var provider = new StreamDataProvider(_engine, PlaybackFormat, stream);
                var player = new SoundPlayer(_engine, PlaybackFormat, provider)
                {
                    Volume = IsMuted ? 0f : (float)(Math.Clamp(VolumePercent, 0, 100) / 100.0)
                };
                player.PlaybackEnded += OnPlaybackEnded;

                _stream = stream;
                _dataProvider = provider;
                _player = player;

                _device.MasterMixer.AddComponent(player);

                if (!_device.IsRunning)
                {
                    _device.Start();
                }

                player.Play();
            });

            IsPlaying = true;
            _visualizerTimer.Start();

            if (_player is not null && _player.Duration > 0)
            {
                DurationSeconds = _player.Duration;
            }

            StatusMessage = $"Playing: {track.Title}";
            SaveLastTrack();
        }
        catch (Exception ex)
        {
            StatusMessage = "Playback error: cannot decode file.";
            System.Diagnostics.Debug.WriteLine($"[MusicPlayer] PlayTrack failed: {ex.Message}");
            IsPlaying = false;
        }
        finally
        {
            IsLoadingAudio = false;
        }
    }

    [RelayCommand]
    public async Task PlayPauseAsync()
    {
        if (_player is null)
        {
            if (CurrentTrack is not null)
            {
                await PlayTrackAsync(CurrentTrack);
            }
            else if (Playlist.Count > 0)
            {
                await PlayTrackAsync(Playlist[0]);
            }
            return;
        }

        if (IsPlaying)
        {
            try
            {
                _player.Pause();
            }
            catch { }

            IsPlaying = false;
            _visualizerTimer.Stop();
            ResetVisualizerBars();
            StatusMessage = "Paused";
        }
        else
        {
            try
            {
                if (_device is not null && !_device.IsRunning)
                {
                    _device.Start();
                }
                _player.Play();
                IsPlaying = true;
                _visualizerTimer.Start();
                StatusMessage = $"Playing: {CurrentTrack?.Title}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MusicPlayer] Resume failed: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void Stop()
    {
        if (_player is not null)
        {
            try
            {
                _player.Stop();
            }
            catch { }
        }

        IsPlaying = false;
        PositionSeconds = 0;
        _visualizerTimer.Stop();
        ResetVisualizerBars();
        StatusMessage = "Stopped";
    }

    [RelayCommand]
    public async Task NextAsync()
    {
        if (Playlist.Count == 0) return;

        if (IsShuffle && Playlist.Count > 1)
        {
            int nextRandom;
            do
            {
                nextRandom = _random.Next(Playlist.Count);
            } while (nextRandom == _currentIndex && Playlist.Count > 1);

            await PlayTrackAsync(Playlist[nextRandom]);
            return;
        }

        var nextIndex = _currentIndex + 1;
        if (nextIndex >= Playlist.Count)
        {
            if (RepeatMode == RepeatMode.RepeatAll)
            {
                nextIndex = 0;
            }
            else
            {
                Stop();
                return;
            }
        }

        await PlayTrackAsync(Playlist[nextIndex]);
    }

    [RelayCommand]
    public async Task PreviousAsync()
    {
        if (Playlist.Count == 0) return;

        // Restart current track if already played past 3 seconds
        if (PositionSeconds > 3 && CurrentTrack is not null)
        {
            CommitSeek(0);
            return;
        }

        var prevIndex = _currentIndex - 1;
        if (prevIndex < 0)
        {
            if (RepeatMode != RepeatMode.RepeatAll)
            {
                CommitSeek(0);
                return;
            }
            prevIndex = Playlist.Count - 1;
        }

        await PlayTrackAsync(Playlist[prevIndex]);
    }

    [RelayCommand]
    public void ToggleRepeatMode()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.RepeatAll,
            RepeatMode.RepeatAll => RepeatMode.RepeatOne,
            _ => RepeatMode.Off
        };
        SaveSettings();
    }

    [RelayCommand]
    public void ToggleShuffle()
    {
        IsShuffle = !IsShuffle;
        SaveSettings();
    }

    [RelayCommand]
    public void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            VolumePercent = _previousVolume > 0 ? _previousVolume : 80;
        }
        else
        {
            _previousVolume = VolumePercent;
            IsMuted = true;
            VolumePercent = 0;
        }
    }

    // --- Scrubbing & Seeking Handlers ---

    public void BeginSeek() => _isSeeking = true;

    public void UpdateSeek(double seconds)
    {
        if (_isSeeking)
        {
            PositionSeconds = Math.Clamp(seconds, 0, Math.Max(0.1, DurationSeconds));
        }
    }

    public void CommitSeek(double seconds)
    {
        _isSeeking = false;
        var clamped = Math.Clamp(seconds, 0, Math.Max(0.1, DurationSeconds));
        PositionSeconds = clamped;

        if (_player is not null)
        {
            Task.Run(() =>
            {
                try
                {
                    _player.Seek((float)clamped);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MusicPlayer] Seek error: {ex.Message}");
                }
            });
        }
    }

    partial void OnVolumePercentChanged(double value)
    {
        if (_player is not null)
        {
            _player.Volume = (float)(Math.Clamp(value, 0, 100) / 100.0);
        }

        if (value > 0 && IsMuted)
        {
            IsMuted = false;
        }

        SaveSettings();
    }

    partial void OnSearchQueryChanged(string value) => ApplySearchFilter();

    partial void OnCurrentTrackChanged(TrackViewModel? oldValue, TrackViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsCurrent = false;
        if (newValue is not null)
        {
            newValue.IsCurrent = true;
            DurationSeconds = newValue.Duration.TotalSeconds;
        }
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _isHandlingPlaybackEnded, 1) == 1) return;

        if (_player is not null)
        {
            try
            {
                _player.PlaybackEnded -= OnPlaybackEnded;
            }
            catch { }
        }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (RepeatMode == RepeatMode.RepeatOne && CurrentTrack is not null)
                {
                    await PlayTrackAsync(CurrentTrack);
                }
                else
                {
                    await NextAsync();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isHandlingPlaybackEnded, 0);
            }
        });
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (_isSeeking || _player is null || !IsPlaying) return;

        try
        {
            PositionSeconds = _player.Time;
        }
        catch { }
    }

    private void OnVisualizerTick(object? sender, EventArgs e)
    {
        if (!IsPlaying)
        {
            ResetVisualizerBars();
            return;
        }

        _visualizerPhase += 0.32;

        Bar1Height = 6 + Math.Abs(Math.Sin(_visualizerPhase * 1.3)) * 14 + (_random.NextDouble() * 3);
        Bar2Height = 8 + Math.Abs(Math.Sin(_visualizerPhase * 1.7 + 0.5)) * 18 + (_random.NextDouble() * 4);
        Bar3Height = 10 + Math.Abs(Math.Cos(_visualizerPhase * 1.1 + 0.9)) * 20 + (_random.NextDouble() * 5);
        Bar4Height = 7 + Math.Abs(Math.Sin(_visualizerPhase * 2.1 + 1.3)) * 16 + (_random.NextDouble() * 4);
        Bar5Height = 5 + Math.Abs(Math.Cos(_visualizerPhase * 1.5 + 1.7)) * 12 + (_random.NextDouble() * 3);
    }

    private void ResetVisualizerBars()
    {
        Bar1Height = 6;
        Bar2Height = 6;
        Bar3Height = 6;
        Bar4Height = 6;
        Bar5Height = 6;
    }

    private static string FormatTime(double totalSeconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return $"{(int)span.TotalMinutes}:{span.Seconds:D2}";
    }

    private void RebuildTrackNumbers()
    {
        for (int i = 0; i < Playlist.Count; i++)
        {
            Playlist[i].TrackNumber = i + 1;
        }
    }

    private void ApplySearchFilter()
    {
        FilteredPlaylist.Clear();
        var query = SearchQuery?.Trim() ?? string.Empty;

        var items = Playlist.AsEnumerable();

        if (FilterFavoritesOnly)
        {
            items = items.Where(t => t.IsFavorite);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                  t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                  t.Album.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var track in items)
        {
            FilteredPlaylist.Add(track);
        }
    }

    private void NotifyCollectionProperties()
    {
        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowSearchFilter));
        OnPropertyChanged(nameof(HasFewTracks));
        OnPropertyChanged(nameof(TotalDurationDisplay));
        OnPropertyChanged(nameof(QueueSummaryDisplay));
        OnPropertyChanged(nameof(UpNextTrack));
        OnPropertyChanged(nameof(UpNextDisplay));
        OnPropertyChanged(nameof(HasUpNextTrack));
        OnPropertyChanged(nameof(FavoritesCount));
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(IsCurrentTrackFavorite));
        OnPropertyChanged(nameof(CurrentTrackFavoriteIconKind));
    }

    private async Task LoadSettingsAsync()
    {
        if (_settingsStore is null) return;

        VolumePercent = _settingsStore.GetSetting(PluginId, SettingVolume, 80.0);
        RepeatMode = _settingsStore.GetSetting(PluginId, SettingRepeatMode, RepeatMode.Off);
        IsShuffle = _settingsStore.GetSetting(PluginId, SettingShuffle, false);

        var savedMode = _settingsStore.GetSetting(PluginId, SettingViewMode, "Player");
        if (Enum.TryParse<PlayerViewMode>(savedMode, true, out var mode))
        {
            CurrentViewMode = mode;
        }

        var savedFavorites = _settingsStore.GetSetting(PluginId, SettingFavorites, Array.Empty<string>());
        _favoritePaths.Clear();
        foreach (var f in savedFavorites) _favoritePaths.Add(f);

        var savedPaths = _settingsStore.GetSetting(PluginId, SettingPlaylist, Array.Empty<string>());
        var validPaths = savedPaths.Where(File.Exists).ToList();

        if (validPaths.Count > 0)
        {
            await AddTracksAsync(validPaths);

            var lastIndex = _settingsStore.GetSetting(PluginId, SettingLastTrackIndex, -1);
            if (lastIndex >= 0 && lastIndex < Playlist.Count)
            {
                _currentIndex = lastIndex;
                CurrentTrack = Playlist[lastIndex];
                DurationSeconds = CurrentTrack.Duration.TotalSeconds;
            }
        }
    }

    private void SavePlaylist()
        => _settingsStore?.SetSetting(PluginId, SettingPlaylist, Playlist.Select(t => t.FilePath).ToArray());

    private void SaveFavorites()
        => _settingsStore?.SetSetting(PluginId, SettingFavorites, _favoritePaths.ToArray());

    private void SaveSettings()
    {
        _settingsStore?.SetSetting(PluginId, SettingVolume, VolumePercent);
        _settingsStore?.SetSetting(PluginId, SettingRepeatMode, RepeatMode);
        _settingsStore?.SetSetting(PluginId, SettingShuffle, IsShuffle);
        _settingsStore?.SetSetting(PluginId, SettingFavorites, _favoritePaths.ToArray());
    }

    private void SaveLastTrack()
        => _settingsStore?.SetSetting(PluginId, SettingLastTrackIndex, _currentIndex);

    private void DisposeCurrentPlayer()
    {
        if (_player is not null)
        {
            try
            {
                _player.PlaybackEnded -= OnPlaybackEnded;
                _player.Stop();
                _device?.MasterMixer.RemoveComponent(_player);
                _player.Dispose();
            }
            catch { }
            finally
            {
                _player = null;
            }
        }

        try { _dataProvider?.Dispose(); } catch { }
        finally { _dataProvider = null; }

        try { _stream?.Dispose(); } catch { }
        finally { _stream = null; }
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;

        _visualizerTimer.Stop();
        _visualizerTimer.Tick -= OnVisualizerTick;

        DisposeCurrentPlayer();

        try
        {
            _device?.Stop();
            _device?.Dispose();
        }
        catch { }

        try
        {
            _engine?.Dispose();
        }
        catch { }
    }
}
