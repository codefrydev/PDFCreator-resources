using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using PdfEditorApp.Messages;

namespace PdfEditorApp.Plugins.ImageEditor.Runner;

public partial class MainWindow : Window
{
    public MainWindow(System.IServiceProvider serviceProvider)
    {
        InitializeComponent();

        var vm = new ImageEditorViewModel(serviceProvider);
        EditorView.DataContext = vm;

        // Subscribe to the back-navigation message the ViewModel fires when the
        // user clicks the ← "Close Studio" button.  In the standalone Runner there
        // is no FryPDF home screen, so we simply close this window cleanly.
        WeakReferenceMessenger.Default.Register<NavigateToHomeMessage>(this, (_, _) =>
        {
            Dispatcher.UIThread.InvokeAsync(Close);
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unregister the message handler to prevent any lingering references
        // after the window is destroyed (zero-dangling-handler rule).
        WeakReferenceMessenger.Default.Unregister<NavigateToHomeMessage>(this);
        base.OnClosed(e);
    }
}
