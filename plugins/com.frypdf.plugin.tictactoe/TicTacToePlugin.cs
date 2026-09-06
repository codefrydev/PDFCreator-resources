using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PdfEditorApp.Core.Plugins;
using PdfEditorApp.Core.Plugins.Descriptors;
using PdfEditorApp.Core.Plugins.Manifests;
using PdfEditorApp.ViewModels;

namespace FryPdf.Plugin.TicTacToe;

/// <summary>
/// Modular plugin contributing an interactive, draggable Tic-Tac-Toe game targeting the 'shell.overlay' slot.
/// Demonstrates clean registration into Overlays, Command Palette, Ribbon, and Status Bar.
/// </summary>
public class TicTacToePlugin : IFryPlugin
{
    public string Id => "com.frypdf.plugin.tictactoe";
    public string Name => "Tic-Tac-Toe";
    public Version Version => new(1, 0, 0);

    public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();
    public IReadOnlyList<Type> ProvidedServices => Array.Empty<Type>();

    public IReadOnlyDictionary<string, PluginSettingDefinition>? SettingsSchema => new Dictionary<string, PluginSettingDefinition>
    {
        ["DefaultMode"] = new()
        {
            Type = "select",
            Label = "Default Game Mode",
            Description = "Initial opponent mode on game launch",
            DefaultValue = "PlayerVsAi",
            Options = ["PlayerVsAi", "TwoPlayers"]
        },
        ["AiDifficulty"] = new()
        {
            Type = "select",
            Label = "AI Difficulty",
            Description = "Tactical strategy level of computer player",
            DefaultValue = "Smart",
            Options = ["Smart", "Unbeatable", "Easy"]
        },
        ["SoundEnabled"] = new()
        {
            Type = "boolean",
            Label = "Sound Effects",
            Description = "Enable sound alerts for moves and game over events",
            DefaultValue = true
        }
    };

    public Task ApplyAsync(IFryPluginContext ctx, CancellationToken ct = default)
    {
        // 1. Register Floating Shell Overlay with StandardCard chrome (Auto M3 titlebar, pin, minimize, close)
        var overlayReg = ctx.RegisterOverlay(new OverlayDescriptor
        {
            Id = Id,
            Title = "🎮 Tic-Tac-Toe",
            Slot = "shell.overlay",
            DefaultWidth = 330,
            DefaultHeight = 440,
            InitialX = null,
            InitialY = 110,
            IsDraggable = true,
            IsMinimizable = true,
            IsClosable = true,
            IconKind = "GamepadVariantOutline",
            ChromeMode = OverlayChromeMode.StandardCard,
            ViewType = typeof(TicTacToeView),
            ViewModelType = typeof(TicTacToeViewModel),
            ViewFactory = _ => new TicTacToeView
            {
                DataContext = new TicTacToeViewModel()
            },
            ViewModelFactory = _ => new TicTacToeViewModel()
        });

        // 2. Register Command in Command Palette (Ctrl+Alt+T / ⌘Alt+T)
        var cmdReg = ctx.RegisterCommand(new CommandPaletteDescriptor
        {
            Id = "cmd.overlay.tictactoe",
            Title = "Play Tic-Tac-Toe (Shell Overlay)",
            Subtitle = "Launch floating Tic-Tac-Toe mini-game with local 2P & AI opponent",
            Category = "Shell Overlays",
            IconKind = "GamepadVariantOutline",
            Shortcut = "Ctrl+Alt+T",
            Order = 96,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // 3. Register Footer Status Bar Widget (Clickable 🎮 Tic-Tac-Toe pill)
        var statusReg = ctx.RegisterStatusBarWidget(new StatusBarWidgetDescriptor
        {
            WidgetId = "frypdf.status.tictactoe",
            Alignment = StatusBarAlignment.Right,
            Order = 16,
            ToolTip = "Play Tic-Tac-Toe (Floating Shell Overlay)",
            Factory = sp =>
            {
                var reg = sp.GetService(typeof(IOverlayRegistry)) as IOverlayRegistry;
                return new StatusBarWidgetViewModel
                {
                    WidgetId = "frypdf.status.tictactoe",
                    Label = "🎮 Tic-Tac-Toe",
                    IconKind = "GamepadVariantOutline",
                    ToolTip = "Toggle floating Tic-Tac-Toe game",
                    IsActive = true,
                    Command = new RelayCommand(() => reg?.ToggleOverlay(Id))
                };
            }
        });

        // 4. Register Quick-Action Pill in the Ribbon's 'View' Tab
        var ribbonReg = ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.tictactoe",
            TabId = "view",
            GroupId = "plugins",
            Label = "Tic-Tac-Toe",
            Tooltip = "Open floating Tic-Tac-Toe game overlay",
            IconKind = "GamepadVariantOutline",
            Order = 54,
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
