using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PdfEditorApp.Core.Plugins;
using PdfEditorApp.Core.Plugins.Descriptors;
using PdfEditorApp.Core.Plugins.Manifests;
using PdfEditorApp.ViewModels;

namespace PdfEditorApp.Plugins.Snake;

/// <summary>
/// Modular plugin contributing a playable, draggable Snake game targeting the 'shell.overlay' slot.
/// Inspired by the Cordis client-side dynamic plugin architecture.
/// </summary>
public class SnakeGamePlugin : IFryPlugin
{
    public string Id => "frypdf.overlay.snake";
    public string Name => "Playable Snake Game";
    public Version Version => new(1, 0, 0);
    public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();

    public IReadOnlyDictionary<string, PluginSettingDefinition>? SettingsSchema => new Dictionary<string, PluginSettingDefinition>
    {
        ["InitialSpeed"] = new()
        {
            Label = "Game Speed",
            Description = "Default tick speed for snake movement",
            Type = "select",
            Options = new() { "Normal", "Fast", "Zen" },
            DefaultValue = "Normal"
        },
        ["WallCollision"] = new()
        {
            Label = "Wall Collision Mode",
            Description = "When enabled, hitting the border causes game over. When disabled, the snake wraps around edges.",
            Type = "boolean",
            DefaultValue = true
        }
    };

    public Task ApplyAsync(IFryPluginContext ctx, CancellationToken ct = default)
    {
        // 1. Register Shell Overlay Descriptor
        ctx.RegisterOverlay(new OverlayDescriptor
        {
            Id = Id,
            Title = "🐍 Snake",
            Slot = "shell.overlay",
            DefaultWidth = 320,
            DefaultHeight = 420,
            InitialX = null,
            InitialY = 100,
            IsDraggable = true,
            IsMinimizable = true,
            IsClosable = true,
            IconKind = "GamepadVariantOutline",
            ChromeMode = OverlayChromeMode.CustomChrome,
            ViewType = typeof(SnakeGameView),
            ViewModelType = typeof(SnakeGameViewModel),
            ViewFactory = sp => new SnakeGameView
            {
                DataContext = new SnakeGameViewModel(sp)
            },
            ViewModelFactory = sp => new SnakeGameViewModel(sp)
        });

        // 2. Register Command Palette action
        ctx.RegisterCommand(new CommandPaletteDescriptor
        {
            Id = "cmd.overlay.snake",
            Title = "Play Snake Game (Shell Overlay)",
            Subtitle = "Launch draggable retro-arcade Snake game floating over the application",
            Category = "Shell Overlays",
            IconKind = "GamepadVariantOutline",
            Shortcut = "Ctrl+Alt+S",
            Order = 95,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // 3. Register Footer Status Bar Widget (Clickable 🐍 Snake pill)
        ctx.RegisterStatusBarWidget(new StatusBarWidgetDescriptor
        {
            WidgetId = "frypdf.status.snake",
            Alignment = StatusBarAlignment.Right,
            Order = 15,
            ToolTip = "Play Snake Game (Floating Shell Overlay)",
            Factory = sp =>
            {
                var reg = sp.GetService(typeof(IOverlayRegistry)) as IOverlayRegistry;
                return new StatusBarWidgetViewModel
                {
                    WidgetId = "frypdf.status.snake",
                    Label = "🐍 Snake",
                    IconKind = "GamepadVariantOutline",
                    ToolTip = "Launch floating Snake game overlay",
                    IsActive = true,
                    Command = new RelayCommand(() =>
                    {
                        reg?.ToggleOverlay(Id);
                    })
                };
            }
        });

        // 4. Register Ribbon Action in View Tab
        ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.snake",
            TabId = "view",
            GroupId = "plugins",
            Label = "Snake Game",
            Tooltip = "Launch floating retro Snake arcade in shell.overlay",
            IconKind = "GamepadVariantOutline",
            Order = 50,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // Reversible effect: Ensure overlay is hidden if plugin is unloaded
        ctx.RegisterEffect(() =>
        {
            if (ctx.TryGetService<IOverlayRegistry>(out var reg))
            {
                reg.HideOverlay(Id);
            }
        });

        return Task.CompletedTask;
    }
}
