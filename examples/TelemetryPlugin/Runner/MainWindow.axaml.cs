using System;
using Avalonia.Controls;

namespace PdfEditorApp.Plugins.Telemetry.Runner;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var settingsStore = new StandaloneSettingsStore();
        var serviceProvider = new StandaloneServiceProvider(settingsStore);
        var viewModel = new DocumentTelemetryViewModel(serviceProvider);

        TelemetryView.DataContext = viewModel;
    }
}
