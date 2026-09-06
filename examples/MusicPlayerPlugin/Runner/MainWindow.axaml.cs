using Avalonia.Controls;

namespace PdfEditorApp.Plugins.MusicPlayer.Runner;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var settingsStore = new StandaloneSettingsStore();
        var serviceProvider = new StandaloneServiceProvider(settingsStore);
        var viewModel = new MusicPlayerViewModel(serviceProvider);

        PlayerView.DataContext = viewModel;
    }
}
