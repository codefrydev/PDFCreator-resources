using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PdfEditorApp.Plugins.Telemetry;

/// <summary>
/// ViewModel for real-time document performance and environment telemetry HUD.
/// </summary>
public partial class DocumentTelemetryViewModel : ObservableObject
{
    private readonly IServiceProvider? _serviceProvider;

    [ObservableProperty]
    private string _memoryAllocatedMb = "0.0 MB";

    [ObservableProperty]
    private int _garbageCollections;

    [ObservableProperty]
    private string _architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();

    [ObservableProperty]
    private string _osDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    [ObservableProperty]
    private string _status = "Optimal (60+ FPS)";

    public DocumentTelemetryViewModel(IServiceProvider? serviceProvider = null)
    {
        _serviceProvider = serviceProvider;
        RefreshMetrics();
    }

    [RelayCommand]
    public void RefreshMetrics()
    {
        long bytes = GC.GetTotalMemory(forceFullCollection: false);
        MemoryAllocatedMb = $"{(bytes / 1024.0 / 1024.0):F1} MB";
        GarbageCollections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
    }

    [RelayCommand]
    public void RunGCTrim()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        RefreshMetrics();
    }
}
