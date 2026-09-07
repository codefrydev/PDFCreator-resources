using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PdfEditorApp.Core.Plugins;
using PdfEditorApp.Core.Plugins.Descriptors;
using PdfEditorApp.Core.Plugins.Manifests;
using PdfEditorApp.ViewModels;

namespace PdfEditorApp.Plugins.ImageEditor;

/// <summary>
/// FryPDF plugin registration for the Image Editor (Canva-style canvas overlay).
/// Contributes to shell.overlay, Command Palette, Status Bar, and Ribbon.
/// </summary>
public class ImageEditorPlugin : IFryPlugin
{
    public string Id => "frypdf.overlay.imageeditor";
    public string Name => "Image Editor";
    public Version Version => new(3, 0, 0);
    public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();

    public IReadOnlyDictionary<string, PluginSettingDefinition>? SettingsSchema =>
        new Dictionary<string, PluginSettingDefinition>
        {
            ["DefaultBackground"] = new()
            {
                Label = "Default Canvas Background",
                Description = "Starting canvas background color when the editor opens.",
                Type = "select",
                Options = new() { "White", "Black", "Transparent" },
                DefaultValue = "White"
            },
            ["DefaultCanvasWidth"] = new()
            {
                Label = "Canvas Width (px)",
                Description = "Default canvas width in pixels.",
                Type = "integer",
                DefaultValue = 640
            },
            ["DefaultCanvasHeight"] = new()
            {
                Label = "Canvas Height (px)",
                Description = "Default canvas height in pixels.",
                Type = "integer",
                DefaultValue = 480
            }
        };

    public Task ApplyAsync(IFryPluginContext ctx, CancellationToken ct = default)
    {
        // 0. Register Full-Viewport Workspace Studio Page in Left Sidebar Navigation
        var navReg = ctx.RegisterNavigationItem(new NavigationItemDescriptor
        {
            Id = "ImageEditorStudio",
            Title = "Image Editor",
            Group = "Library",                           // Integrated into sidebar Library section
            IconKind = "ImageEditOutline",               // Canva-style palette/canvas icon
            BadgeText = "Studio",                        // Status pill badge
            BadgeColorHex = "#2563EB",                   // Blue accent color
            Order = 170,                                 // Positioned in Library section
            DisplayMode = NavigationDisplayMode.FullViewport, // Edge-to-edge canvas without outer scroll
            HideTopSearchBar = false,
            ViewFactory = sp => new ImageEditorView
            {
                DataContext = new ImageEditorViewModel(sp)
            }
        });
        // 1. Register Shell Overlay — StandardCard chrome for automatic M3 draggable
        //    header, pin/minimize/close, and free resize grips (see host's ShellOverlayHost).
        var overlayReg = ctx.RegisterOverlay(new OverlayDescriptor
        {
            Id = Id,
            Title = "Image Editor",
            Slot = "shell.overlay",
            DefaultWidth = 900,
            DefaultHeight = 640,
            InitialX = null,
            InitialY = 60,
            IsDraggable = true,
            IsMinimizable = true,
            IsClosable = true,
            IsResizable = true,
            MinWidth = 680,
            MinHeight = 520,
            MaxWidth = 1400,
            MaxHeight = 1100,
            IconKind = "ImageEditOutline",
            ChromeMode = OverlayChromeMode.StandardCard,
            ViewType = typeof(ImageEditorView),
            ViewModelType = typeof(ImageEditorViewModel),
            ViewFactory = sp => new ImageEditorView
            {
                DataContext = new ImageEditorViewModel(sp)
            },
            ViewModelFactory = sp => new ImageEditorViewModel(sp)
        });

        // 2. Command Palette
        var cmdReg = ctx.RegisterCommand(new CommandPaletteDescriptor
        {
            Id = "cmd.overlay.imageeditor",
            Title = "Open Image Editor (Shell Overlay)",
            Subtitle = "Launch Canva-style floating image editor with shapes, text, and PNG export",
            Category = "Shell Overlays",
            IconKind = "ImageEditOutline",
            Shortcut = "Ctrl+Alt+I",
            Order = 80,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // 3. Status Bar Widget
        var statusReg = ctx.RegisterStatusBarWidget(new StatusBarWidgetDescriptor
        {
            WidgetId = "frypdf.status.imageeditor",
            Alignment = StatusBarAlignment.Right,
            Order = 12,
            ToolTip = "Open Image Editor (Floating Shell Overlay)",
            Factory = sp =>
            {
                var reg = sp.GetService(typeof(IOverlayRegistry)) as IOverlayRegistry;
                return new StatusBarWidgetViewModel
                {
                    WidgetId = "frypdf.status.imageeditor",
                    Label = "🎨 Image Editor",
                    IconKind = "ImageEditOutline",
                    ToolTip = "Launch floating Canva-style image editor",
                    IsActive = true,
                    Command = new RelayCommand(() => reg?.ToggleOverlay(Id))
                };
            }
        });

        // 4. Ribbon Action
        var ribbonReg = ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.imageeditor",
            TabId = "view",
            GroupId = "plugins",
            Label = "Image Editor",
            Tooltip = "Launch floating Canva-style image editor in shell.overlay",
            IconKind = "ImageEditOutline",
            Order = 40,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // 5. Reversible effects for 100% clean teardown — dispose every registration
        //    handle before hiding the overlay, mirroring MusicPlayerPlugin's pattern.
        ctx.RegisterEffect(() =>
        {
            navReg.Dispose();
            overlayReg.Dispose();
            cmdReg.Dispose();
            statusReg.Dispose();
            ribbonReg.Dispose();

            if (ctx.TryGetService<IOverlayRegistry>(out var reg))
            {
                reg.HideOverlay(Id);
            }
        });

        return Task.CompletedTask;
    }
}
