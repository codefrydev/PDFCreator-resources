using System.Collections.Generic;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.ImageEditor.Models.Templates;

/// <summary>
/// 8 starter templates, each buildable entirely from existing element types (rectangles,
/// ellipses, text, and image placeholders — ImageElement already renders a camera-icon
/// placeholder box when Source is null, exactly like a real design tool's "drop your photo
/// here" slot). No new asset/resource dependency.
/// </summary>
public static class TemplateLibrary
{
    public static IReadOnlyList<TemplateDefinition> All { get; } = new[]
    {
        BlankPresentationSlide(),
        CertificateOfCompletion(),
        SimpleFlyer(),
        PhotoCollage(),
        QuoteCard(),
        CoverPage(),
        SocialMediaPost(),
        StickyNote(),
    };

    /// <summary>Auto-fit text (Width/Height computed from the string) — safe to set X/Y/Text/
    /// FontSize/etc in any order since no wrap-width dependency exists.</summary>
    private static TextElement CreateText(double x, double y, string text, double fontSize, Color color,
        FontWeight weight = FontWeight.Normal, FontStyle style = FontStyle.Normal)
    {
        var t = new TextElement
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            FontStyle = style,
            ForegroundColor = color,
            X = x,
            Y = y,
        };
        return t;
    }

    /// <summary>Wrapped, fixed-width text — auto-fit width must be disabled BEFORE Width is
    /// set (otherwise the immediately-following remeasure would overwrite it), so this is
    /// built statement-by-statement rather than via a single object initializer.</summary>
    private static TextElement CreateWrappedText(double x, double y, double width, string text, double fontSize,
        FontWeight weight, Color color, TextAlignment alignment = TextAlignment.Left)
    {
        var t = new TextElement { IsWidthAutoFitEnabled = false };
        t.Text = text;
        t.FontSize = fontSize;
        t.FontWeight = weight;
        t.ForegroundColor = color;
        t.IsWrapEnabled = true;
        t.Alignment = alignment;
        t.Width = width;
        t.X = x;
        t.Y = y;
        return t;
    }

    private static TemplateDefinition BlankPresentationSlide() => new()
    {
        Name = "Blank Presentation Slide",
        Category = "Documents",
        CanvasWidth = 1280,
        CanvasHeight = 720,
        BackgroundColor = Colors.White,
        BuildElements = () =>
        {
            var els = new List<CanvasElement>();
            void Add(CanvasElement e) { e.ZIndex = els.Count; els.Add(e); }
            var accent = Color.FromArgb(255, 79, 70, 229);

            // "Blank" doesn't have to mean "bare" — a subtle edge accent + underline reads as a
            // deliberate minimalist theme rather than an unfinished/empty thumbnail.
            Add(new RectangleElement { X = 0, Y = 0, Width = 14, Height = 720, FillColor = accent, StrokeThickness = 0, CornerRadius = 0 });
            Add(CreateText(100, 280, "Click to add title", 54, Color.FromArgb(255, 30, 30, 30), FontWeight.Bold));
            Add(CreateText(100, 380, "Click to add subtitle", 24, Color.FromArgb(180, 80, 80, 80)));
            Add(new RectangleElement { X = 100, Y = 440, Width = 80, Height = 6, FillColor = accent, StrokeThickness = 0, CornerRadius = 3 });
            return els;
        },
    };

    private static TemplateDefinition CertificateOfCompletion() => new()
    {
        Name = "Certificate of Completion",
        Category = "Documents",
        CanvasWidth = 1100,
        CanvasHeight = 850,
        BackgroundColor = Color.FromRgb(0xFB, 0xF7, 0xEF),
        BuildElements = () =>
        {
            var els = new List<CanvasElement>();
            void Add(CanvasElement e) { e.ZIndex = els.Count; els.Add(e); }
            var gold = Color.FromRgb(0xC9, 0xA2, 0x27);
            var ink = Color.FromRgb(0x3A, 0x2E, 0x14);
            var inkSoft = Color.FromRgb(0x6B, 0x5D, 0x3E);

            Add(new RectangleElement { X = 40, Y = 40, Width = 1020, Height = 770, FillColor = Colors.Transparent, StrokeColor = gold, StrokeThickness = 6, CornerRadius = 0 });
            Add(new RectangleElement { X = 60, Y = 60, Width = 980, Height = 730, FillColor = Colors.Transparent, StrokeColor = gold, StrokeThickness = 1.5, CornerRadius = 0 });
            Add(CreateWrappedText(150, 160, 800, "CERTIFICATE OF COMPLETION", 40, FontWeight.Bold, ink, TextAlignment.Center));
            Add(CreateWrappedText(150, 320, 800, "This certifies that", 20, FontWeight.Normal, inkSoft, TextAlignment.Center));
            Add(CreateWrappedText(150, 370, 800, "[ Recipient Name ]", 44, FontWeight.Bold, ink, TextAlignment.Center));
            Add(CreateWrappedText(150, 460, 800, "has successfully completed the requirements of this course", 18, FontWeight.Normal, inkSoft, TextAlignment.Center));
            Add(new RectangleElement { X = 400, Y = 700, Width = 300, Height = 2, FillColor = ink, StrokeThickness = 0 });
            Add(CreateText(460, 715, "Signature", 14, inkSoft));

            // Gold seal badge — a classic certificate motif, missing entirely before.
            Add(new EllipseElement { X = 860, Y = 640, Width = 120, Height = 120, FillColor = gold, StrokeColor = ink, StrokeThickness = 2 });
            Add(new StarElement
            {
                X = 880, Y = 660, Width = 80, Height = 80, PointCount = 5, InnerRadiusRatio = 0.45,
                FillColor = Color.FromRgb(0xFB, 0xF7, 0xEF), StrokeThickness = 0,
            });
            return els;
        },
    };

    private static TemplateDefinition SimpleFlyer() => new()
    {
        Name = "Simple Flyer",
        Category = "Marketing",
        CanvasWidth = 800,
        CanvasHeight = 1200,
        BackgroundColor = Colors.White,
        BuildElements = () =>
        {
            var els = new List<CanvasElement>();
            void Add(CanvasElement e) { e.ZIndex = els.Count; els.Add(e); }
            var accent = Color.FromArgb(255, 79, 70, 229);

            Add(new RectangleElement { X = 0, Y = 0, Width = 800, Height = 320, FillColor = accent, StrokeThickness = 0, CornerRadius = 0 });
            Add(CreateText(60, 120, "EVENT TITLE", 56, Colors.White, FontWeight.Bold));
            Add(CreateText(60, 200, "A short, punchy tagline goes here", 22, Color.FromArgb(230, 255, 255, 255)));

            // A flyer with 700px of blank white between the header and the details reads as
            // unfinished — a photo placeholder is the single biggest thing this was missing.
            Add(new ImageElement { X = 60, Y = 360, Width = 680, Height = 380 });

            Add(CreateText(60, 780, "Date & Time", 24, Color.FromArgb(255, 30, 30, 30), FontWeight.Bold));
            Add(CreateText(60, 820, "Saturday, January 1 · 6:00 PM", 18, Color.FromArgb(255, 90, 90, 90)));
            Add(CreateText(60, 900, "Location", 24, Color.FromArgb(255, 30, 30, 30), FontWeight.Bold));
            Add(CreateText(60, 940, "123 Main Street, Your City", 18, Color.FromArgb(255, 90, 90, 90)));
            Add(new RectangleElement { X = 60, Y = 1020, Width = 260, Height = 70, FillColor = accent, StrokeThickness = 0, CornerRadius = 35 });
            Add(CreateText(110, 1040, "RSVP NOW", 22, Colors.White, FontWeight.Bold));
            return els;
        },
    };

    private static TemplateDefinition PhotoCollage() => new()
    {
        Name = "Photo Collage (3-up)",
        Category = "Social Media",
        CanvasWidth = 1200,
        CanvasHeight = 800,
        // A white background made the ~20px gaps between the 3 panels nearly invisible against
        // the placeholders' own light-grey fill — a dark neutral makes the grid actually read as
        // a grid, and matches a common photo-collage aesthetic besides.
        BackgroundColor = Color.FromRgb(0x18, 0x18, 0x1C),
        BuildElements = () =>
        {
            var els = new List<CanvasElement>();
            void Add(CanvasElement e) { e.ZIndex = els.Count; els.Add(e); }

            Add(new ImageElement { X = 20, Y = 20, Width = 373, Height = 700 });
            Add(new ImageElement { X = 413, Y = 20, Width = 373, Height = 700 });
            Add(new ImageElement { X = 806, Y = 20, Width = 373, Height = 700 });
            Add(CreateWrappedText(20, 740, 1160, "Your Caption Here", 22, FontWeight.Bold, Colors.White, TextAlignment.Center));
            return els;
        },
    };

    private static TemplateDefinition QuoteCard() => new()
    {
        Name = "Quote / Callout Card",
        Category = "Social Media",
        CanvasWidth = 1080,
        CanvasHeight = 1080,
        BackgroundColor = Color.FromRgb(0x18, 0x18, 0x22),
        BuildElements = () =>
        {
            var els = new List<CanvasElement>();
            void Add(CanvasElement e) { e.ZIndex = els.Count; els.Add(e); }

            Add(new RectangleElement
            {
                X = 60, Y = 60, Width = 960, Height = 960,
                FillColor = Colors.Transparent,
                Gradient = GradientFill.CreateDefault(GradientKind.Linear,
                    Color.FromArgb(255, 79, 70, 229), Color.FromArgb(255, 236, 72, 153)),
                StrokeThickness = 0,
                CornerRadius = 32,
            });
            Add(CreateWrappedText(140, 400, 800, "“Design is not just what it looks like.\nDesign is how it works.”",
                46, FontWeight.Bold, Colors.White, TextAlignment.Center));
            Add(CreateWrappedText(140, 760, 800, "— Steve Jobs", 22, FontWeight.Normal,
                Color.FromArgb(220, 255, 255, 255), TextAlignment.Center));
            return els;
        },
    };

    private static TemplateDefinition CoverPage() => new()
    {
        Name = "Cover Page",
        Category = "Documents",
        CanvasWidth = 850,
        CanvasHeight = 1100,
        BackgroundColor = Colors.White,
        BuildElements = () =>
        {
            var els = new List<CanvasElement>();
            void Add(CanvasElement e) { e.ZIndex = els.Count; els.Add(e); }

            Add(new RectangleElement
            {
                X = 0, Y = 0, Width = 850, Height = 1100,
                FillColor = Colors.Transparent,
                Gradient = GradientFill.CreateDefault(GradientKind.Linear,
                    Color.FromArgb(255, 30, 41, 59), Color.FromArgb(255, 79, 70, 229)),
                StrokeThickness = 0,
            });
            Add(new ImageElement { X = 375, Y = 80, Width = 100, Height = 100 });
            Add(CreateWrappedText(80, 480, 690, "Report Title Goes Here", 64, FontWeight.Bold, Colors.White, TextAlignment.Center));
            Add(CreateWrappedText(80, 620, 690, "A subtitle or short description for the cover page", 24, FontWeight.Normal,
                Color.FromArgb(220, 255, 255, 255), TextAlignment.Center));
            Add(CreateText(80, 1020, "Prepared by Your Name · 2026", 16, Color.FromArgb(200, 255, 255, 255)));
            return els;
        },
    };

    private static TemplateDefinition SocialMediaPost() => new()
    {
        Name = "Social Media Post",
        Category = "Social Media",
        CanvasWidth = 1080,
        CanvasHeight = 1080,
        BackgroundColor = Colors.White,
        BuildElements = () =>
        {
            var els = new List<CanvasElement>();
            void Add(CanvasElement e) { e.ZIndex = els.Count; els.Add(e); }

            Add(new RectangleElement
            {
                X = 0, Y = 0, Width = 1080, Height = 1080,
                FillColor = Colors.Transparent,
                Gradient = GradientFill.CreateDefault(GradientKind.Radial,
                    Color.FromArgb(255, 236, 72, 153), Color.FromArgb(255, 79, 70, 229)),
                StrokeThickness = 0,
            });
            Add(CreateWrappedText(100, 420, 880, "Big Bold Headline", 84, FontWeight.Bold, Colors.White, TextAlignment.Center));
            Add(CreateText(420, 980, "@yourhandle", 22, Color.FromArgb(230, 255, 255, 255)));
            return els;
        },
    };

    private static TemplateDefinition StickyNote() => new()
    {
        Name = "Sticky Note / Label",
        Category = "Creative",
        CanvasWidth = 400,
        CanvasHeight = 400,
        // A plain white canvas around a small tilted square just looked like empty space — a
        // neutral "desk surface" tone gives the note something to sit on.
        BackgroundColor = Color.FromRgb(0xEC, 0xE9, 0xE2),
        BuildElements = () =>
        {
            var els = new List<CanvasElement>();
            void Add(CanvasElement e) { e.ZIndex = els.Count; els.Add(e); }

            // Faux drop shadow: no per-element shadow property exists yet, so a darker, offset
            // copy underneath fakes the same effect — the missing sense of depth was the main
            // thing making this read as a flat colored square rather than a physical note.
            Add(new RectangleElement
            {
                X = 32, Y = 32, Width = 340, Height = 340,
                FillColor = Color.FromArgb(70, 0, 0, 0), StrokeThickness = 0, CornerRadius = 4,
                RotationDegrees = -3,
            });
            Add(new RectangleElement
            {
                X = 20, Y = 20, Width = 340, Height = 340,
                FillColor = Color.FromArgb(255, 253, 224, 71), StrokeThickness = 0, CornerRadius = 4,
                RotationDegrees = -3,
            });
            var noteText = CreateWrappedText(60, 155, 260, "Don't forget!", 32, FontWeight.Bold, Color.FromArgb(255, 60, 50, 10), TextAlignment.Center);
            noteText.RotationDegrees = -3; // matches the note's own tilt instead of floating upright over it
            Add(noteText);
            return els;
        },
    };
}
