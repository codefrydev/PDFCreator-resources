using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PdfEditorApp.Core.Plugins;
using PdfEditorApp.Core.Plugins.Descriptors;
using PdfEditorApp.Core.Plugins.Manifests;
using PdfEditorApp.ViewModels;

namespace PdfEditorApp.Plugins.Scratchpad;

/// <summary>
/// Plugin contributing a floating Markdown Review Scratchpad targeting the 'shell.overlay' slot.
/// Uses OverlayChromeMode.StandardCard for automatic Material Design 3 window frame and dragging physics.
/// </summary>
public class ScratchpadPlugin : IFryPlugin
{
    public string Id => "frypdf.overlay.scratchpad";
    public string Name => "Review Scratchpad";
    public Version Version => new(1, 0, 0);
    public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();

    public IReadOnlyDictionary<string, PluginSettingDefinition>? SettingsSchema => new Dictionary<string, PluginSettingDefinition>
    {
        ["AutoClearOnClose"] = new()
        {
            Label = "Auto-Clear Notes on Close",
            Description = "Automatically clear notes text when closing the scratchpad overlay",
            Type = "boolean",
            DefaultValue = false
        }
    };

    public Task ApplyAsync(IFryPluginContext ctx, CancellationToken ct = default)
    {
        // 1. Register Overlay with StandardCard chrome (Auto M3 draggable header, pin, minimize, close)
        ctx.RegisterOverlay(new OverlayDescriptor
        {
            Id = Id,
            Title = "📝 Scratchpad",
            Slot = "shell.overlay",
            DefaultWidth = 360,
            DefaultHeight = 340,
            InitialX = null,
            InitialY = 120,
            IsDraggable = true,
            IsMinimizable = true,
            IsClosable = true,
            IconKind = "NotebookEditOutline",
            ChromeMode = OverlayChromeMode.StandardCard,
            ViewType = typeof(ScratchpadView),
            ViewModelType = typeof(ScratchpadViewModel),
            ViewFactory = sp => new ScratchpadView
            {
                DataContext = new ScratchpadViewModel(sp)
            },
            ViewModelFactory = sp => new ScratchpadViewModel(sp)
        });

        // 2. Register Command Palette
        ctx.RegisterCommand(new CommandPaletteDescriptor
        {
            Id = "cmd.overlay.scratchpad",
            Title = "Toggle Review Scratchpad (Shell Overlay)",
            Subtitle = "Open floating note pad to take notes during document review",
            Category = "Shell Overlays",
            IconKind = "NotebookEditOutline",
            Shortcut = "Ctrl+Alt+N",
            Order = 90,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // 3. Register Footer Status Bar Widget
        ctx.RegisterStatusBarWidget(new StatusBarWidgetDescriptor
        {
            WidgetId = "frypdf.status.scratchpad",
            Alignment = StatusBarAlignment.Right,
            Order = 14,
            ToolTip = "Toggle Review Scratchpad",
            Factory = sp =>
            {
                var reg = sp.GetService(typeof(IOverlayRegistry)) as IOverlayRegistry;
                return new StatusBarWidgetViewModel
                {
                    WidgetId = "frypdf.status.scratchpad",
                    Label = "📝 Notes",
                    IconKind = "NotebookEditOutline",
                    ToolTip = "Toggle floating review scratchpad",
                    IsActive = true,
                    Command = new RelayCommand(() => reg?.ToggleOverlay(Id))
                };
            }
        });

        // 4. Register Ribbon Action in View Tab
        ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.scratchpad",
            TabId = "view",
            GroupId = "plugins",
            Label = "Scratchpad",
            Tooltip = "Open floating review scratchpad in shell.overlay",
            IconKind = "NotebookEditOutline",
            Order = 52,
            Action = sp =>
            {
                if (sp.GetService(typeof(IOverlayRegistry)) is IOverlayRegistry reg)
                {
                    reg.ToggleOverlay(Id);
                }
            }
        });

        // Clean rollback on unload
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
