using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PdfEditorApp.Core.Plugins;
using PdfEditorApp.Core.Plugins.Descriptors;
using PdfEditorApp.Core.Plugins.Manifests;
using PdfEditorApp.ViewModels;

namespace PdfEditorApp.Plugins.Telemetry;

/// <summary>
/// Plugin contributing a live document diagnostics & memory telemetry HUD targeting 'shell.overlay'.
/// Uses OverlayChromeMode.StandardCard for automatic Material Design 3 frame and dragging physics.
/// </summary>
public class DocumentTelemetryPlugin : IFryPlugin
{
    public string Id => "frypdf.overlay.telemetry";
    public string Name => "Document Telemetry HUD";
    public Version Version => new(1, 0, 0);
    public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();

    public IReadOnlyDictionary<string, PluginSettingDefinition>? SettingsSchema => null;

    public Task ApplyAsync(IFryPluginContext ctx, CancellationToken ct = default)
    {
        // 1. Register Overlay with StandardCard chrome
        ctx.RegisterOverlay(new OverlayDescriptor
        {
            Id = Id,
            Title = "⚡ Telemetry HUD",
            Slot = "shell.overlay",
            DefaultWidth = 320,
            DefaultHeight = 250,
            InitialX = null,
            InitialY = 160,
            IsDraggable = true,
            IsMinimizable = true,
            IsClosable = true,
            IconKind = "ChartTimelineVariant",
            ChromeMode = OverlayChromeMode.StandardCard,
            ViewType = typeof(DocumentTelemetryView),
            ViewModelType = typeof(DocumentTelemetryViewModel),
            ViewFactory = sp => new DocumentTelemetryView
            {
                DataContext = new DocumentTelemetryViewModel(sp)
            },
            ViewModelFactory = sp => new DocumentTelemetryViewModel(sp)
        });

        // 2. Register Command Palette
        ctx.RegisterCommand(new CommandPaletteDescriptor
        {
            Id = "cmd.overlay.telemetry",
            Title = "Toggle Telemetry HUD (Shell Overlay)",
            Subtitle = "Display real-time memory and document engine diagnostics",
            Category = "Shell Overlays",
            IconKind = "ChartTimelineVariant",
            Shortcut = "Ctrl+Alt+T",
            Order = 92,
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
            WidgetId = "frypdf.status.telemetry",
            Alignment = StatusBarAlignment.Right,
            Order = 13,
            ToolTip = "Toggle Document Telemetry HUD",
            Factory = sp =>
            {
                var reg = sp.GetService(typeof(IOverlayRegistry)) as IOverlayRegistry;
                return new StatusBarWidgetViewModel
                {
                    WidgetId = "frypdf.status.telemetry",
                    Label = "⚡ HUD",
                    IconKind = "ChartTimelineVariant",
                    ToolTip = "Toggle real-time engine telemetry",
                    IsActive = true,
                    Command = new RelayCommand(() => reg?.ToggleOverlay(Id))
                };
            }
        });

        // 4. Register Ribbon Action in View Tab
        ctx.RegisterRibbonAction(new RibbonActionDescriptor
        {
            Id = "frypdf.ribbon.action.telemetry",
            TabId = "view",
            GroupId = "plugins",
            Label = "Telemetry HUD",
            Tooltip = "Open real-time diagnostics HUD in shell.overlay",
            IconKind = "ChartTimelineVariant",
            Order = 54,
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
