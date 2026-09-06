using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
    static MusicPlayerPlugin()
    {
        EnsureManagedPluginDependenciesResolvable();
        EnsureNativeAudioLibrariesLoaded();
    }

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
        },
        ["AutoScanMusicOnLaunch"] = new()
        {
            Type = "boolean",
            Label = "Auto-Discover Music",
            Description = "Automatically discover tracks from default OS Music library if playlist is empty",
            DefaultValue = false
        }
    };

    public Task ApplyAsync(IFryPluginContext ctx, CancellationToken ct = default)
    {
        // Ensure managed plugin dependencies and native miniaudio audio library are resolved in isolated ALC contexts
        EnsureManagedPluginDependenciesResolvable();
        EnsureNativeAudioLibrariesLoaded();

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

    private static bool _nativeLoaded;
    private static readonly object _nativeLock = new();

    /// <summary>
    /// Explicitly resolves and pre-loads the miniaudio native shared library for SoundFlow
    /// within isolated AssemblyLoadContext (ALC) plugin environments like FryPDF.
    /// </summary>
    public static void EnsureNativeAudioLibrariesLoaded()
    {
        if (_nativeLoaded) return;
        lock (_nativeLock)
        {
            if (_nativeLoaded) return;

            try
            {
                var pluginAssembly = typeof(MusicPlayerPlugin).Assembly;
                var pluginDir = Path.GetDirectoryName(pluginAssembly.Location);
                if (string.IsNullOrEmpty(pluginDir))
                {
                    pluginDir = AppContext.BaseDirectory;
                }

                string rid;
                string libFileName;
                if (OperatingSystem.IsMacOS())
                {
                    rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
                    libFileName = "libminiaudio.dylib";
                }
                else if (OperatingSystem.IsWindows())
                {
                    rid = RuntimeInformation.ProcessArchitecture switch
                    {
                        Architecture.Arm64 => "win-arm64",
                        Architecture.X86 => "win-x86",
                        _ => "win-x64"
                    };
                    libFileName = "miniaudio.dll";
                }
                else if (OperatingSystem.IsLinux())
                {
                    rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
                    libFileName = "libminiaudio.so";
                }
                else
                {
                    return;
                }

                var candidates = new[]
                {
                    Path.Combine(pluginDir, "runtimes", rid, "native", libFileName),
                    Path.Combine(pluginDir, libFileName),
                    Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", libFileName),
                    Path.Combine(AppContext.BaseDirectory, libFileName)
                };

                string? foundPath = null;
                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        foundPath = candidate;
                        break;
                    }
                }

                if (foundPath != null)
                {
                    // 1. Explicitly load into process address space so the dynamic linker caches it
                    NativeLibrary.TryLoad(foundPath, out _);

                    // 2. Set DllImportResolver on the SoundFlow assembly so any [LibraryImport("miniaudio")] delegates directly to this handle
                    try
                    {
                        NativeLibrary.SetDllImportResolver(typeof(SoundFlow.Components.SoundPlayer).Assembly, (name, asm, searchPath) =>
                        {
                            if (name.Equals("miniaudio", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("libminiaudio", StringComparison.OrdinalIgnoreCase))
                            {
                                if (NativeLibrary.TryLoad(foundPath, out var handle))
                                {
                                    return handle;
                                }
                            }
                            return IntPtr.Zero;
                        });
                    }
                    catch
                    {
                        // SetDllImportResolver can only be registered once per assembly
                    }
                }

                _nativeLoaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MusicPlayer] Native audio library setup warning: {ex.Message}");
            }
        }
    }

    private static bool _managedResolversInstalled;
    private static readonly object _managedLock = new();

    /// <summary>
    /// Ensures that bundled managed dependencies (TagLibSharp.dll, SoundFlow.dll, etc.) can be located
    /// and loaded inside isolated collectible AssemblyLoadContexts or via AppDomain fallbacks.
    /// </summary>
    public static void EnsureManagedPluginDependenciesResolvable()
    {
        if (_managedResolversInstalled) return;
        lock (_managedLock)
        {
            if (_managedResolversInstalled) return;

            try
            {
                var pluginAssembly = typeof(MusicPlayerPlugin).Assembly;
                var pluginDir = Path.GetDirectoryName(pluginAssembly.Location);
                if (string.IsNullOrEmpty(pluginDir))
                {
                    pluginDir = AppContext.BaseDirectory;
                }

                // 1. Register with the specific AssemblyLoadContext that loaded this plugin assembly
                var alc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(pluginAssembly);
                if (alc != null && alc != System.Runtime.Loader.AssemblyLoadContext.Default)
                {
                    alc.Resolving += (context, asmName) =>
                    {
                        var candidate = Path.Combine(pluginDir, $"{asmName.Name}.dll");
                        if (File.Exists(candidate))
                        {
                            return context.LoadFromAssemblyPath(candidate);
                        }
                        return null;
                    };
                }

                // 2. Register fallback with AppDomain.CurrentDomain
                AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                {
                    try
                    {
                        var reqName = new System.Reflection.AssemblyName(args.Name).Name;
                        if (string.IsNullOrEmpty(reqName)) return null;

                        var candidate = Path.Combine(pluginDir, $"{reqName}.dll");
                        if (File.Exists(candidate))
                        {
                            return System.Reflection.Assembly.LoadFrom(candidate);
                        }
                    }
                    catch { }
                    return null;
                };

                _managedResolversInstalled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MusicPlayer] Managed resolver setup warning: {ex.Message}");
            }
        }
    }
}

