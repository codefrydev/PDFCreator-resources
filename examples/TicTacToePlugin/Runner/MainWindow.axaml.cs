using System;
using Avalonia.Controls;

namespace FryPdf.Plugin.TicTacToe.Runner;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var settingsStore = new StandaloneSettingsStore();
        var serviceProvider = new StandaloneServiceProvider(settingsStore);
        var viewModel = new TicTacToeViewModel();

        GameView.DataContext = viewModel;
    }
}
