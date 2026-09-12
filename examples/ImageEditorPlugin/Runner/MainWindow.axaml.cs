using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace PdfEditorApp.Plugins.ImageEditor.Runner;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider serviceProvider)
    {
        InitializeComponent();

        var vm = new ImageEditorViewModel(serviceProvider);
        vm.RequestClose = () => Dispatcher.UIThread.InvokeAsync(Close);
        EditorView.DataContext = vm;
    }
}
