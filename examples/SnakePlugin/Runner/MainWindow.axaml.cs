using System;
using Avalonia.Controls;

namespace PdfEditorApp.Plugins.Snake.Runner;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var settingsStore = new StandaloneSettingsStore();
        var serviceProvider = new StandaloneServiceProvider(settingsStore);
        var viewModel = new SnakeGameViewModel(serviceProvider);

        GameView.DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        GameView.Focus();
    }
}
