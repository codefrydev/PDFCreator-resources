using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace PdfEditorApp.Plugins.MusicPlayer;

/// <summary>Maps a <see cref="RepeatMode"/> to the Material icon glyph shown on the repeat toggle.</summary>
public class RepeatModeIconConverter : IValueConverter
{
    public static readonly RepeatModeIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is RepeatMode mode
            ? mode switch
            {
                RepeatMode.RepeatOne => MaterialIconKind.RepeatOnce,
                RepeatMode.RepeatAll => MaterialIconKind.RepeatVariant,
                _ => MaterialIconKind.RepeatOff
            }
            : MaterialIconKind.RepeatOff;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
