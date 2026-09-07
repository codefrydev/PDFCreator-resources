using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PdfEditorApp.Plugins.ImageEditor.Runner;

public partial class MainWindow : Window
{
    public MainWindow(System.IServiceProvider serviceProvider)
    {
        InitializeComponent();

        var vm = new ImageEditorViewModel(serviceProvider);
        EditorView.DataContext = vm;
    }
}
