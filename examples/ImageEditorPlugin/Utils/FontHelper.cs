using System;
using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Utils;

/// <summary>
/// Plugin-local font resolution and caching helper for ImageEditorPlugin.
/// </summary>
public static class FontHelper
{
    private static readonly ConcurrentDictionary<string, FontFamily> FontFamilyCache = new(StringComparer.Ordinal);

    private static readonly string UserFontDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FryPDF", "Fonts");

    public static event Action? FontsChanged;

    public static void RegisterFontFamily(string fontName)
    {
        if (!string.IsNullOrWhiteSpace(fontName))
        {
            FontFamilyCache.TryRemove(fontName, out _);
            FontsChanged?.Invoke();
        }
    }

    public static void InvalidateFontFamilyCache()
    {
        FontFamilyCache.Clear();
        FontsChanged?.Invoke();
    }

    public static FontFamily CreateFontFamily(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
            return FontFamily.Default;

        return FontFamilyCache.GetOrAdd(fontName, static name =>
        {
            string cleanName = name.Replace(" ", "");
            string ttfPath = Path.Combine(UserFontDirectory, $"{cleanName}.ttf");

            if (File.Exists(ttfPath))
            {
                return new FontFamily(
                    $"file://{UserFontDirectory}#{name}, avares://PdfEditorApp/Assets/Fonts#{name}, {name}");
            }

            return new FontFamily($"avares://PdfEditorApp/Assets/Fonts#{name}, {name}");
        });
    }

    private static readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), Typeface>
        TypefaceCache = new();

    static FontHelper()
    {
        FontsChanged += () => TypefaceCache.Clear();
    }

    public static Typeface CreateTypeface(string? fontName, bool isBold, bool isItalic)
    {
        var key = (fontName ?? string.Empty, isBold, isItalic);

        return TypefaceCache.GetOrAdd(key, static k => new Typeface(
            CreateFontFamily(k.Family),
            k.Italic ? FontStyle.Italic : FontStyle.Normal,
            k.Bold ? FontWeight.Bold : FontWeight.Normal));
    }

    public static string GetSafeFallback(string? requestedFamily)
    {
        if (string.IsNullOrWhiteSpace(requestedFamily)) return "Open Sans";

        string lc = requestedFamily.ToLowerInvariant();
        if (lc.Contains("sc") || lc.Contains("hans") || lc.Contains("chinese") || lc.Contains("simsun") || lc.Contains("yahei")) return "Noto Sans SC";
        if (lc.Contains("tc") || lc.Contains("hant") || lc.Contains("mingliu")) return "Noto Sans TC";
        if (lc.Contains("jp") || lc.Contains("japanese") || lc.Contains("gothic") || lc.Contains("mincho")) return "Noto Sans JP";
        if (lc.Contains("kr") || lc.Contains("korean") || lc.Contains("hangul") || lc.Contains("malgun")) return "Noto Sans KR";
        if (lc.Contains("devanagari") || lc.Contains("hindi")) return "Noto Sans Devanagari";
        if (lc.Contains("tamil")) return "Noto Sans Tamil";
        if (lc.Contains("telugu")) return "Noto Sans Telugu";
        if (lc.Contains("arabic") || lc.Contains("urdu")) return "Noto Sans Arabic";
        if (lc.Contains("hebrew")) return "Noto Sans Hebrew";
        if (lc.Contains("thai")) return "Noto Sans Thai";
        if (lc.Contains("gujarati")) return "Noto Sans Gujarati";
        if (lc.Contains("kannada")) return "Noto Sans Kannada";
        if (lc.Contains("bengali")) return "Noto Sans Bengali";
        if (lc.Contains("malayalam")) return "Noto Sans Malayalam";

        if (lc.Contains("serif") || lc.Contains("times") || lc.Contains("georgia") ||
            lc.Contains("garamond") || lc.Contains("baskerville"))
            return "PT Serif";

        if (lc.Contains("mono") || lc.Contains("code") || lc.Contains("courier"))
            return "Fira Code";

        return "Open Sans";
    }
}
