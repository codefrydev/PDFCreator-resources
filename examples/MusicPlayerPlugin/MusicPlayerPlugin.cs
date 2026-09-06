using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PdfEditorApp.Core.Plugins;
using PdfEditorApp.Core.Plugins.Descriptors;
using PdfEditorApp.Core.Plugins.Manifests;
using PdfEditorApp.ViewModels;

namespace PdfEditorApp.Plugins.MusicPlayer;

/// <summary>
/// Plugin contributing a floating Music Player targeting the 'shell.overlay' slot.
/// Uses OverlayChromeMode.StandardCard for automatic Material Design 3 window frame and dragging physics.
/// </summary>
public class MusicPlayerPlugin : IFryPlugin
{
    public string Id => "frypdf.overlay.musicplayer";
    public string Name => "Music Player";
    public Version Version => new(1, 0, 0);
    public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();

    public IReadOnlyDictionary<string, PluginSettingDefinition>? SettingsSchema => new Dictionary<string, PluginSettingDefinition>
    {
        ["Volume"] = new()
        {
            Type = "number",
            Label = "Default Volume",
            Description = "Initial playback volume (0-100)",
            DefaultValue = 80
        },
        ["RepeatMode"] = new()
        {
            Type = "select",
            Label = "Repeat Mode",
            Description = "Default playlist repeat behavior on launch",
            DefaultValue = "Off",
            Options = ["Off", "RepeatOne", "RepeatAll"]
        },
        ["RememberLastPlaylist"] = new()
        {
            Type = "boolean",
            Label = "Remember Last Playlist",
            Description = "Reload the last playlist and track position on startup",
            DefaultValue = true
        }
    };

    public Task ApplyAsync(IFryPluginContext ctx, CancellationToken ct = default)
    {
        // 1. Register Overlay with StandardCard chrome (Auto M3 draggable header, pin, minimize, close)
        var overlayReg = ctx.RegisterOverlay(new OverlayDescriptor
        {
            Id = Id,
            Title = "🎵 Music Player",
            Slot = "shell.overlay",
            DefaultWidth = 380,
            DefaultHeight = 560,
            InitialX = null,
            InitialY = 100,
            IsDraggable = true,
            IsMinimizable = true,
            IsClosable = true,
            IconKind = "MusicNote",
            ChromeMode = OverlayChromeMode.StandardCard,
            ViewType = typeof(MusicPlayerView),
            ViewModelType = typeof(MusicPlayerViewModel),
            ViewFactory = sp => new MusicPlayerView
            {
                DataContext = new MusicPlayerViewModel(sp)
            },
            ViewModelFactory = sp => new MusicPlayerViewModel(sp)
        });

        // 2. Register Command Palette
        var cmdReg = ctx.RegisterCommand(new CommandPaletteDescriptor
        {
            Id = "cmd.overlay.musicplayer",
            Title = "Toggle Music Player (Shell Overlay)",
            Subtitle = "Open floating music player with playlist, transport controls, and ID3 metadata",
            Category = "Shell Overlays",
            IconKind = "MusicNote",
            Shortcut = "Ctrl+Alt+P",
            Order = 91,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // 3. Register Footer Status Bar Widget
        var statusReg = ctx.RegisterStatusBarWidget(new StatusBarWidgetDescriptor
        {
            WidgetId = "frypdf.status.musicplayer",
            Alignment = StatusBarAlignment.Right,
            Order = 13,
            ToolTip = "Toggle Music Player",
            Factory = sp =>
            {
                var reg = sp.GetService(typeof(IOverlayRegistry)) as IOverlayRegistry;
                return new StatusBarWidgetViewModel
                {
                    WidgetId = "frypdf.status.musicplayer",
                    Label = "🎵 Music",
                    IconKind = "MusicNote",
                    ToolTip = "Toggle floating music player",
                    IsActive = true,
                    Command = new RelayCommand(() => reg?.ToggleOverlay(Id))
                };
            }
        });

        // 4. Register Ribbon Action in View Tab
        var ribbonReg = ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.musicplayer",
            TabId = "view",
            GroupId = "plugins",
            Label = "Music Player",
            Tooltip = "Open floating music player in shell.overlay",
            IconKind = "MusicNote",
            Order = 55,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // 5. Register Reversible Effects for 100% Clean Teardown
        ctx.RegisterEffect(() =>
        {
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
