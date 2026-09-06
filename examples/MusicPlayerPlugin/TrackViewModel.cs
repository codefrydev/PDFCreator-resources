using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfEditorApp.Plugins.MusicPlayer;

/// <summary>
/// Represents a single track in the playlist, with metadata read from the audio file's tags.
/// </summary>
public partial class TrackViewModel : ObservableObject
{
    public string FilePath { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _artist;

    [ObservableProperty]
    private string _album;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private Bitmap? _coverArt;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private int _trackNumber;

    public string TrackNumberDisplay => TrackNumber > 0 ? $"{TrackNumber:D2}" : "•";

    public string FileExtension
    {
        get
        {
            var ext = Path.GetExtension(FilePath);
            return string.IsNullOrEmpty(ext) ? "AUDIO" : ext.TrimStart('.').ToUpperInvariant();
        }
    }

    public string DurationDisplay => $"{(int)Duration.TotalMinutes}:{Duration.Seconds:D2}";

    public TrackViewModel(string filePath, string title, string artist, string album, TimeSpan duration, Bitmap? coverArt, int trackNumber = 0)
    {
        FilePath = filePath;
        _title = title;
        _artist = artist;
        _album = album;
        _duration = duration;
        _coverArt = coverArt;
        _trackNumber = trackNumber;
    }

    /// <summary>
    /// Asynchronously parses audio metadata and cover art on a background thread to prevent UI thread blocking.
    /// </summary>
    public static System.Threading.Tasks.Task<TrackViewModel> FromFileAsync(string filePath, int trackNumber = 0)
        => System.Threading.Tasks.Task.Run(() => FromFile(filePath, trackNumber));

    /// <summary>
    /// Builds a TrackViewModel from an audio file's ID3/Vorbis/etc. tags, falling back to the
    /// filename when tags are missing or unreadable so a single bad file never blocks the rest.
    /// </summary>
    public static TrackViewModel FromFile(string filePath, int trackNumber = 0)
    {
        var fallbackTitle = Path.GetFileNameWithoutExtension(filePath);

        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var tag = tagFile.Tag;

            var title = string.IsNullOrWhiteSpace(tag.Title) ? fallbackTitle : tag.Title.Trim();
            var artist = tag.FirstPerformer ?? tag.FirstAlbumArtist ?? "Unknown Artist";
            var album = string.IsNullOrWhiteSpace(tag.Album) ? "Unknown Album" : tag.Album.Trim();
            var duration = tagFile.Properties?.Duration ?? TimeSpan.Zero;

            Bitmap? coverArt = null;
            var picture = tag.Pictures.Length > 0 ? tag.Pictures[0] : null;
            if (picture is not null && picture.Data.Data.Length > 0)
            {
                try
                {
                    using var stream = new MemoryStream(picture.Data.Data);
                    // Decode to max 400px width to save memory and avoid LOH allocations
                    coverArt = Bitmap.DecodeToWidth(stream, 400);
                }
                catch
                {
                    // Fall back to direct bitmap or no cover if decoding fails
                    try
                    {
                        using var stream = new MemoryStream(picture.Data.Data);
                        coverArt = new Bitmap(stream);
                    }
                    catch
                    {
                        coverArt = null;
                    }
                }
            }

            return new TrackViewModel(filePath, title, artist, album, duration, coverArt, trackNumber);
        }
        catch
        {
            return new TrackViewModel(filePath, fallbackTitle, "Unknown Artist", "Unknown Album", TimeSpan.Zero, null, trackNumber);
        }
    }
}
