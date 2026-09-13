using System;
using Avalonia.Controls;
using FryPdf.Plugin.Chess.ViewModels;

namespace FryPdf.Plugin.Chess.Runner;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var settingsStore = new StandaloneSettingsStore();
        var serviceProvider = new StandaloneServiceProvider(settingsStore);
        var viewModel = new ChessViewModel();

        GameView.DataContext = viewModel;
    }
}
