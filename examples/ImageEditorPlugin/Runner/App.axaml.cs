using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PdfEditorApp.Plugins.ImageEditor.Runner;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsStore = new StandaloneSettingsStore();
            var serviceProvider = new StandaloneServiceProvider(settingsStore);

            desktop.MainWindow = new MainWindow(serviceProvider);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
