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

    /// <summary>Milliseconds of audio per device period. See <see cref="BuildDeviceConfig"/>.</summary>
    private const int PeriodSizeMs = 50;

    /// <summary>Number of device periods, so total buffered audio is Periods x PeriodSizeMs.</summary>
    private const int PeriodCount = 4;

    /// <summary>
    /// Samples per channel that <see cref="ChunkedDataProvider"/> decodes ahead of the callback.
    /// </summary>
    /// <remarks>
    /// At 44.1 kHz this is roughly a second of read-ahead — comfortably more than any single
    /// host stall, and cheap in memory next to the decoded bitmaps this plugin already holds.
    /// </remarks>
    private const int DecodeChunkSamples = 44100;

    private readonly IPluginSettingsStore? _settingsStore;
    private MiniAudioEngine? _engine;
    private AudioPlaybackDevice? _device;

    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _visualizerTimer;
    private readonly Random _random = new();
    private readonly HashSet<string> _favoritePaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Serializes mutation of the audio graph (player, provider, stream, mixer membership).
    /// </summary>
    /// <remarks>
    /// Track loading, disposal and seeking all ran on unsynchronized thread-pool threads while
    /// the audio callback pulled from the same provider — <c>DisposeCurrentPlayer</c> could
    /// dispose the stream out from under an in-flight seek, and the empty <c>catch</c> blocks
    /// hid the resulting use-after-dispose. Held only across bounded field swaps and SoundFlow
    /// calls; UI-thread readers snapshot the fields instead of taking this, so a slow track
    /// load can never stall a frame.
    /// </remarks>
    private readonly object _graphLock = new();

    private FileStream? _stream;
    private SoundFlow.Interfaces.ISoundDataProvider? _dataProvider;
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

    // --- Live 5-Bar Equalizer Visualizer ---
    // These are ScaleY factors in 0..1 applied to a fixed-height bar via RenderTransform, not
    // heights. They used to be bound straight to Border.Height, and Height is layout-affecting:
    // rewriting all five at 20 Hz invalidated measure/arrange up the parent chain 20 times a
    // second, on the UI thread the host editor shares. Eight bars are bound (five in Player
    // view, three in Mini) and IsVisible=false does not unbind, so every one of them was live
    // in every view mode. A ScaleY transform is GPU-composited and triggers no layout pass.
    // See .agents/rules/performance_and_zero_lag_mandate.md section 6.
    [ObservableProperty] private double _bar1Scale = BarScaleFloor;
    [ObservableProperty] private double _bar2Scale = 0.71;
    [ObservableProperty] private double _bar3Scale = 1.0;
    [ObservableProperty] private double _bar4Scale = 0.79;
    [ObservableProperty] private double _bar5Scale = 0.5;

    /// <summary>Rendered height of an equalizer bar in dips; scale factors are relative to it.</summary>
    private const double BarTrackHeight = 14.0;

    /// <summary>Resting scale, so idle bars stay visible as dots rather than vanishing.</summary>
    private const double BarScaleFloor = 6.0 / BarTrackHeight;

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

        // Both timers used the parameterless DispatcherTimer constructor, which Avalonia
        // documents as DispatcherPriority.Background — *below* Input. Host editor interaction
        // therefore starved them, which is the visible half of the "music lags when I use the
        // editor" report: the elapsed time and the equalizer freeze and then catch up in a
        // burst. Priorities are now explicit, and split by what the timer is for.
        //
        // The position readout is functional and cheap (4 Hz), so it runs at Input: not starved
        // below interaction, but not preempting it either.
        _positionTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _positionTimer.Tick += OnPositionTimerTick;

        // The equalizer is pure decoration at 20 Hz, so it stays below Input deliberately —
        // it *should* yield to the user interacting with the host. Raising it would repeat the
        // mistake WavySlider made by animating at Render priority.
        _visualizerTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _visualizerTimer.Tick += OnVisualizerTick;

        _ = LoadSettingsAsync();
    }

    private void InitializeAudioEngine()
    {
        try
        {
            MusicPlayerPlugin.EnsureNativeAudioLibrariesLoaded();
            _engine = new MiniAudioEngine(Array.Empty<MiniAudioBackend>());
            _device = _engine.InitializePlaybackDevice(null, PlaybackFormat, BuildDeviceConfig());
            _device.Start();
        }
        catch (Exception ex)
        {
            StatusMessage = "Audio engine initialization failed.";
            System.Diagnostics.Debug.WriteLine($"[MusicPlayer] Audio init error: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the playback device config with enough buffer to ride out host hitches.
    /// </summary>
    /// <remarks>
    /// This used to pass a default <see cref="MiniAudioDeviceConfig"/>, leaving period size and
    /// count at miniaudio's low-latency defaults — roughly 10 ms x 3 periods, about 30 ms of
    /// slack in total. SoundFlow decodes on miniaudio's callback thread, which is a
    /// CLR-attached managed thread, so any GC pause or scheduling delay longer than that
    /// underruns and is audible.
    ///
    /// Music playback has no latency requirement at all: nothing is synchronised to it and
    /// nobody is monitoring live input. Trading latency for resilience is free here, so buffer
    /// for ~200 ms and survive a host stall of that length without a dropout.
    /// </remarks>
    private static MiniAudioDeviceConfig BuildDeviceConfig() => new()
    {
        PeriodSizeInMilliseconds = PeriodSizeMs,
        Periods = PeriodCount
    };

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
                // One critical section for teardown and setup together: a half-swapped graph
                // (old stream disposed, new player not yet in the mixer) must never be visible
                // to a concurrent seek or to another track change.
                lock (_graphLock)
                {
                    DisposeCurrentPlayerLocked();

                    if (!File.Exists(track.FilePath))
                    {
                        throw new FileNotFoundException("Track file not found.", track.FilePath);
                    }

                    // FileOptions.SequentialScan lets the OS read ahead; the provider below decodes
                    // in chunks off the audio callback, so this stream is no longer touched from a
                    // real-time thread.
                    var stream = new FileStream(
                        track.FilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 64 * 1024,
                        FileOptions.SequentialScan);

                    // Was StreamDataProvider, which is pull-on-demand: the miniaudio callback thread
                    // did the FileStream read and the MP3 decode itself, so disk contention or a GC
                    // pause landed directly on the audio deadline. ChunkedDataProvider buffers ahead
                    // instead, which is what SoundFlow's own NetworkDataProvider does "to prevent
                    // network issues from crashing the audio thread".
                    var provider = new ChunkedDataProvider(_engine, PlaybackFormat, stream, DecodeChunkSamples);
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
                }
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
                lock (_graphLock)
                {
                    // Re-read under the lock: the field may have been swapped or nulled by a
                    // track change between this seek being queued and it running.
                    var player = _player;
                    if (player is null) return;

                    try
                    {
                        player.Seek((float)clamped);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MusicPlayer] Seek error: {ex.Message}");
                    }
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

    /// <summary>
    /// Re-filters the queue after the user stops typing.
    /// </summary>
    /// <remarks>
    /// This used to call <see cref="ApplySearchFilter"/> on every keystroke, and that method
    /// does <c>Clear()</c> then one <c>Add()</c> per match on a collection bound to the queue
    /// list — so each character re-realized every visible track card. Debounced per
    /// .agents/rules/performance_and_zero_lag_mandate.md section 4 (150-250ms for text filters).
    /// </remarks>
    partial void OnSearchQueryChanged(string value)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SearchDebounceMs, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested) return;

            // The filter mutates FilteredPlaylist, which is bound to the queue list, so it has
            // to run on the UI thread. Post rather than Invoke: nothing here waits on the result.
            Dispatcher.UIThread.Post(() =>
            {
                if (!token.IsCancellationRequested) ApplySearchFilter();
            }, DispatcherPriority.Background);
        }, token);
    }

    /// <summary>Quiet period before the queue search re-filters, in milliseconds.</summary>
    private const int SearchDebounceMs = 180;

    private CancellationTokenSource? _searchDebounceCts;

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

    /// <summary>
    /// Runs the position timer only while audio is actually playing.
    /// </summary>
    /// <remarks>
    /// The timer used to be started in the constructor and never stopped, so it ticked every
    /// 250 ms for the life of the process even with an empty queue — reading _player.Time and
    /// fanning out to four dependent display properties and two two-way-bound sliders. Reading
    /// player state also takes SoundFlow's internal locks from the UI thread, which the
    /// real-time audio callback contends for, so an idle player was needlessly poking the
    /// audio graph four times a second.
    /// </remarks>
    partial void OnIsPlayingChanged(bool value)
    {
        if (value)
        {
            if (!_positionTimer.IsEnabled) _positionTimer.Start();
        }
        else
        {
            if (_positionTimer.IsEnabled) _positionTimer.Stop();
        }
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

        // Same waveform as before, expressed as a 0..1 scale of BarTrackHeight. The old dip
        // values ran well past the 14px container and were simply clipped, so clamping here
        // preserves the look.
        Bar1Scale = BarScale(6 + Math.Abs(Math.Sin(_visualizerPhase * 1.3)) * 14 + (_random.NextDouble() * 3));
        Bar2Scale = BarScale(8 + Math.Abs(Math.Sin(_visualizerPhase * 1.7 + 0.5)) * 18 + (_random.NextDouble() * 4));
        Bar3Scale = BarScale(10 + Math.Abs(Math.Cos(_visualizerPhase * 1.1 + 0.9)) * 20 + (_random.NextDouble() * 5));
        Bar4Scale = BarScale(7 + Math.Abs(Math.Sin(_visualizerPhase * 2.1 + 1.3)) * 16 + (_random.NextDouble() * 4));
        Bar5Scale = BarScale(5 + Math.Abs(Math.Cos(_visualizerPhase * 1.5 + 1.7)) * 12 + (_random.NextDouble() * 3));
    }

    private void ResetVisualizerBars()
    {
        Bar1Scale = BarScaleFloor;
        Bar2Scale = BarScaleFloor;
        Bar3Scale = BarScaleFloor;
        Bar4Scale = BarScaleFloor;
        Bar5Scale = BarScaleFloor;
    }

    /// <summary>Converts a legacy bar height in dips to a clamped 0..1 ScaleY factor.</summary>
    private static double BarScale(double heightInDips)
        => Math.Clamp(heightInDips / BarTrackHeight, BarScaleFloor, 1.0);

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

    /// <summary>
    /// Tears the current player, provider and stream down in dependency order.
    /// </summary>
    /// <remarks>
    /// Stop, then remove from the mixer, then dispose: the component must be out of the graph
    /// before its provider and stream go away, or the audio callback can read a disposed
    /// stream. Callers may be on any thread, so <see cref="_graphLock"/> serializes this
    /// against track loading and seeking.
    /// </remarks>
    private void DisposeCurrentPlayer()
    {
        lock (_graphLock)
        {
            DisposeCurrentPlayerLocked();
        }
    }

    private void DisposeCurrentPlayerLocked()
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
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = null;

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
