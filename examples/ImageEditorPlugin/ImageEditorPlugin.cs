using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using PdfEditorApp.Core.Plugins;
using PdfEditorApp.Core.Plugins.Descriptors;
using PdfEditorApp.Core.Plugins.Manifests;

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
    public bool AutoOpenOverlay => false;
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
            Group = "Overview",                          // Integrated into Overview right after New Document
            IconKind = "ImageEditOutline",               // Canva-style palette/canvas icon
            BadgeText = "Studio",                        // Status pill badge
            BadgeColorHex = "#2563EB",                   // Blue accent color
            Order = 35,                                  // Sits between New Document (30) and All Tools (40)
            DisplayMode = NavigationDisplayMode.FullViewport, // Edge-to-edge canvas; hides Home sidebar
            HideTopSearchBar = true,                     // Hides global search bar so Studio bar takes full top
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
            AutoOpenOnStartup = false,
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
                return new Button
                {
                    Content = "🎨 Image Editor",
                    Command = new RelayCommand(() => reg?.ToggleOverlay(Id))
                };
            }
        });

        // 4. Dynamic Ribbon Integration — Contributes a dedicated "Image" Ribbon Tab
        //    plus actions in Insert and Plugins tabs so users can launch/interact directly.
        var ribbonDisposables = new List<IDisposable>();

        // 4a. Dynamic "Image" Ribbon Tab
        var ribbonTabReg = ctx.RegisterRibbonTab(new RibbonTabDescriptor
        {
            Id = "image",
            Title = "Image",
            Order = 45,
            IsDynamic = true
        });
        ribbonDisposables.Add(ribbonTabReg);

        // 4b. Dynamic Ribbon Groups inside the "Image" Tab
        var groupStudioReg = ctx.RegisterRibbonGroup(new RibbonGroupDescriptor
        {
            Id = "studio",
            TabId = "image",
            Title = "Image Studio",
            Order = 10
        });
        ribbonDisposables.Add(groupStudioReg);

        var groupDrawReg = ctx.RegisterRibbonGroup(new RibbonGroupDescriptor
        {
            Id = "draw",
            TabId = "image",
            Title = "Shapes & Text",
            Order = 20
        });
        ribbonDisposables.Add(groupDrawReg);

        var groupTemplatesReg = ctx.RegisterRibbonGroup(new RibbonGroupDescriptor
        {
            Id = "templates",
            TabId = "image",
            Title = "Templates & Media",
            Order = 30
        });
        ribbonDisposables.Add(groupTemplatesReg);

        var groupExportReg = ctx.RegisterRibbonGroup(new RibbonGroupDescriptor
        {
            Id = "export",
            TabId = "image",
            Title = "Export & Actions",
            Order = 40
        });
        ribbonDisposables.Add(groupExportReg);

        // 4c. Primary Action in Studio Group: "Image Editor" (opens the overlay window)
        ribbonDisposables.Add(ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.imageeditor.open",
            TabId = "image",
            GroupId = "studio",
            Label = "Image Editor",
            Tooltip = "Open Canva-Style Image Editor overlay window",
            IconKind = "ImageEditOutline",
            Order = 10,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ShowOverlay(Id);
                }
            }
        }));

        ribbonDisposables.Add(ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.imageeditor.new",
            TabId = "image",
            GroupId = "studio",
            Label = "New Canvas",
            Tooltip = "Create a fresh design canvas in the Image Editor",
            IconKind = "Artboard",
            Order = 20,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ShowOverlay(Id);
                }
            }
        }));

        // 4d. Draw & Add Actions
        ribbonDisposables.Add(ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.imageeditor.text",
            TabId = "image",
            GroupId = "draw",
            Label = "Add Text",
            Tooltip = "Add styled text and typography in Image Editor",
            IconKind = "FormatText",
            Order = 10,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ShowOverlay(Id);
                }
            }
        }));

        ribbonDisposables.Add(ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.imageeditor.shape",
            TabId = "image",
            GroupId = "draw",
            Label = "Add Shape",
            Tooltip = "Add geometric rectangles, ellipses, or arrows",
            IconKind = "ShapeOutline",
            Order = 20,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ShowOverlay(Id);
                }
            }
        }));

        // 4e. Templates & Media Actions
        ribbonDisposables.Add(ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.imageeditor.template",
            TabId = "image",
            GroupId = "templates",
            Label = "Templates",
            Tooltip = "Open starter template gallery (social posts, banners, cards)",
            IconKind = "ViewGridOutline",
            Order = 10,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ShowOverlay(Id);
                }
            }
        }));

        ribbonDisposables.Add(ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.imageeditor.import",
            TabId = "image",
            GroupId = "templates",
            Label = "Import Image",
            Tooltip = "Import external image file into canvas",
            IconKind = "ImagePlus",
            Order = 20,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ShowOverlay(Id);
                }
            }
        }));

        // 4f. Export Actions
        ribbonDisposables.Add(ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.imageeditor.export",
            TabId = "image",
            GroupId = "export",
            Label = "Export PNG",
            Tooltip = "Export canvas graphics as high-res PNG image",
            IconKind = "Download",
            Order = 10,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ShowOverlay(Id);
                }
            }
        }));

        // 4g. Integration in standard Built-in Tabs (Insert & Plugins)
        ribbonDisposables.Add(ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.imageeditor.insert",
            TabId = "insert",
            GroupId = "media",
            Label = "Image Studio",
            Tooltip = "Launch floating Canva-style image editor overlay",
            IconKind = "ImageEditOutline",
            Order = 40,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ShowOverlay(Id);
                }
            }
        }));

        ribbonDisposables.Add(ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.imageeditor.plugins",
            TabId = "plugins",
            GroupId = "creative",
            Label = "Image Editor",
            Tooltip = "Launch floating Canva-style image editor overlay",
            IconKind = "ImageEditOutline",
            Order = 20,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ShowOverlay(Id);
                }
            }
        }));

        // 5. Reversible effects for 100% clean teardown — dispose every registration
        //    handle before hiding the overlay, mirroring MusicPlayerPlugin's pattern.
        ctx.RegisterEffect(() =>
        {
            navReg.Dispose();
            overlayReg.Dispose();
            cmdReg.Dispose();
            statusReg.Dispose();

            foreach (var r in ribbonDisposables)
            {
                r.Dispose();
            }

            if (ctx.TryGetService<IOverlayRegistry>(out var reg))
            {
                reg.HideOverlay(Id);
            }
        });

        return Task.CompletedTask;
    }
}
