using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using FryPdf.Plugin.Chess.ViewModels;
using FryPdf.Plugin.Chess.Views;
using PdfEditorApp.Core.Plugins;
using PdfEditorApp.Core.Plugins.Descriptors;
using PdfEditorApp.Core.Plugins.Manifests;

namespace FryPdf.Plugin.Chess;

/// <summary>
/// Modular plugin contributing an interactive, draggable Chess game (CPU vs User)
/// targeting the 'shell.overlay' slot with M3 Expressive aesthetics.
/// </summary>
public class ChessPlugin : IFryPlugin
{
    public string Id => "com.frypdf.plugin.chess";
    public string Name => "Chess";
    public Version Version => new(1, 0, 0);

    public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();
    public IReadOnlyList<Type> ProvidedServices => Array.Empty<Type>();

    public IReadOnlyDictionary<string, PluginSettingDefinition>? SettingsSchema => new Dictionary<string, PluginSettingDefinition>
    {
        ["AiDifficulty"] = new()
        {
            Type = "select",
            Label = "CPU Difficulty",
            Description = "Tactical depth and calculation level of the computer opponent",
            DefaultValue = "Medium",
            Options = ["Easy", "Medium", "Hard"]
        },
        ["BoardTheme"] = new()
        {
            Type = "select",
            Label = "Board Theme",
            Description = "Visual color styling of the 8x8 chessboard",
            DefaultValue = "Emerald",
            Options = ["Emerald", "ClassicWood", "MidnightSlate"]
        },
        ["ShowLegalMoveHints"] = new()
        {
            Type = "boolean",
            Label = "Highlight Legal Moves",
            Description = "Display target indicators on valid destination squares",
            DefaultValue = true
        },
        ["ShowCoordinates"] = new()
        {
            Type = "boolean",
            Label = "Show Board Coordinates",
            Description = "Display ranks (1-8) and files (a-h) along the board edge",
            DefaultValue = true
        }
    };

    public Task ApplyAsync(IFryPluginContext ctx, CancellationToken ct = default)
    {
        // 1. Register Floating Shell Overlay with StandardCard chrome (Auto M3 titlebar, pin, minimize, close)
        var overlayReg = ctx.RegisterOverlay(new OverlayDescriptor
        {
            Id = Id,
            Title = "♟️ Chess",
            Slot = "shell.overlay",
            DefaultWidth = 410,
            DefaultHeight = 540,
            InitialX = null,
            InitialY = 90,
            IsDraggable = true,
            IsMinimizable = true,
            IsClosable = true,
            IconKind = "ChessKnight",
            ChromeMode = OverlayChromeMode.StandardCard,
            ViewType = typeof(ChessView),
            ViewModelType = typeof(ChessViewModel),
            ViewFactory = _ => new ChessView
            {
                DataContext = new ChessViewModel()
            },
            ViewModelFactory = _ => new ChessViewModel()
        });

        // 2. Register Command in Command Palette (Ctrl+Alt+C)
        var cmdReg = ctx.RegisterCommand(new CommandPaletteDescriptor
        {
            Id = "cmd.overlay.chess",
            Title = "Play Chess (Shell Overlay)",
            Subtitle = "Launch floating Chess game with CPU opponent (Easy, Medium, Hard)",
            Category = "Shell Overlays",
            IconKind = "ChessKnight",
            Shortcut = "Ctrl+Alt+C",
            Order = 95,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // 3. Register Footer Status Bar Widget (Clickable ♟️ Chess pill)
        var statusReg = ctx.RegisterStatusBarWidget(new StatusBarWidgetDescriptor
        {
            WidgetId = "frypdf.status.chess",
            Alignment = StatusBarAlignment.Right,
            Order = 15,
            ToolTip = "Play Chess (Floating Shell Overlay)",
            Factory = sp =>
            {
                var reg = sp.GetService(typeof(IOverlayRegistry)) as IOverlayRegistry;
                return new Button
                {
                    Content = "♟️ Chess",
                    Command = new RelayCommand(() => reg?.ToggleOverlay(Id))
                };
            }
        });

        // 4. Register Quick-Action Pill in Ribbon's 'View' Tab
        var ribbonReg = ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.chess",
            TabId = "view",
            GroupId = "plugins",
            Label = "Chess",
            Tooltip = "Open floating Chess game overlay",
            IconKind = "ChessKnight",
            Order = 55,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // 5. Register Reversible Effects for 100% Clean LIFO Teardown
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
