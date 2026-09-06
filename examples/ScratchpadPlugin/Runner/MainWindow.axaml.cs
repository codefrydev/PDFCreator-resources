using System;
using Avalonia.Controls;

namespace PdfEditorApp.Plugins.Scratchpad.Runner;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var settingsStore = new StandaloneSettingsStore();
        var serviceProvider = new StandaloneServiceProvider(settingsStore);
        var viewModel = new ScratchpadViewModel(serviceProvider);

        ScratchpadView.DataContext = viewModel;
    }
}
