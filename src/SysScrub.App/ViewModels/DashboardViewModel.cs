using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;

namespace SysScrub.App.ViewModels;

/// <summary>
/// Panel. Yalnızca gerçekten ölçebildiğimiz veriyi gösterir; henüz gelmemiş
/// modüllerin sayıları uydurulmaz.
///
/// Temizlik kartı, Temizleyici ekranıyla aynı görünüm modelini paylaşır: burada
/// başlatılan tarama oraya geçtiğinde hazır bekliyor olur, iki kez taranmaz.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly SystemInfoService _systemInfo;
    private readonly MainWindowViewModel _shell;

    [ObservableProperty]
    private string _osLabel = string.Empty;

    [ObservableProperty]
    private string _machineLabel = string.Empty;

    [ObservableProperty]
    private string _uptimeLabel = string.Empty;

    [ObservableProperty]
    private string _elevationLabel = string.Empty;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private string _systemDriveName = "—";

    [ObservableProperty]
    private string _systemDriveFree = "—";

    [ObservableProperty]
    private string _systemDriveDetail = string.Empty;

    [ObservableProperty]
    private double _systemDriveUsedRatio;

    [ObservableProperty]
    private bool _isSystemDriveCriticallyFull;

    [ObservableProperty]
    private string _memoryLabel = "—";

    [ObservableProperty]
    private string _memoryDetail = string.Empty;

    [ObservableProperty]
    private double _memoryUsedRatio;

    public DashboardViewModel(SystemInfoService systemInfo, CleanerViewModel cleaner, MainWindowViewModel shell)
    {
        _systemInfo = systemInfo;
        _shell = shell;
        Cleaner = cleaner;
        Drives = [];

        // Temizleyici tarama bitirdiğinde halkanın ve sayacın da güncellenmesi gerekiyor.
        Cleaner.PropertyChanged += OnCleanerChanged;

        Refresh();
    }

    /// <summary>Temizleyici ekranıyla paylaşılan görünüm modeli.</summary>
    public CleanerViewModel Cleaner { get; }

    public ObservableCollection<DriveRow> Drives { get; }

    // ---------------------------------------------------------------- temizlik kartı

    public bool HasScanned => Cleaner.Stage is CleanerStage.Reviewing or CleanerStage.Finished;

    public bool IsScanning => Cleaner.Stage == CleanerStage.Scanning;

    /// <summary>Halkanın dolum oranı: tarama sırasında gerçek ilerleme, bittiğinde tam tur.</summary>
    public double ScanRingProgress => Cleaner.Stage switch
    {
        CleanerStage.Scanning => Cleaner.ProgressFraction,
        CleanerStage.Reviewing or CleanerStage.Finished => 1d,
        _ => 0d
    };

    public string ScanValueLabel => Cleaner.Stage switch
    {
        CleanerStage.Scanning => Cleaner.FoundLabel,
        CleanerStage.Reviewing or CleanerStage.Finished => Cleaner.FoundLabel,
        _ => "—"
    };

    public string ScanCaption => Cleaner.Stage switch
    {
        CleanerStage.Scanning => "taranıyor...",
        CleanerStage.Reviewing when Cleaner.FoundFiles == 0 => "temizlenecek bir şey yok",
        CleanerStage.Reviewing or CleanerStage.Finished => $"{Cleaner.FoundFiles:N0} dosya temizlenebilir",
        _ => "henüz taranmadı"
    };

    public string ScanHint => Cleaner.Stage switch
    {
        CleanerStage.Scanning => "Tarama sürüyor. Dosyalar yalnızca listeleniyor, hiçbir şey silinmiyor.",
        CleanerStage.Reviewing when Cleaner.FoundFiles > 0 =>
            "Ne silineceğine sen karar ver. Temizleyici ekranında kural kural inceleyebilirsin.",
        CleanerStage.Reviewing => "Sistem temiz görünüyor.",
        CleanerStage.Finished => "Temizlik tamamlandı. Ayrıntılar Zaman tüneli ekranında.",
        _ => "Tarama, dosyaları listeler ama hiçbir şey silmez. Ne silineceğine sen karar verirsin."
    };

    [RelayCommand]
    private void OpenCleaner()
    {
        _shell.SelectedItem = _shell.Items.FirstOrDefault(i => i.TemplateKey == "CleanerPageTemplate")
                              ?? _shell.SelectedItem;
    }

    private void OnCleanerChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasScanned));
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(ScanRingProgress));
        OnPropertyChanged(nameof(ScanValueLabel));
        OnPropertyChanged(nameof(ScanCaption));
        OnPropertyChanged(nameof(ScanHint));
    }

    public void Dispose() => Cleaner.PropertyChanged -= OnCleanerChanged;

    public void Refresh()
    {
        SystemSnapshot snapshot = _systemInfo.Capture();

        OsLabel = snapshot.OperatingSystem;
        MachineLabel = snapshot.MachineName;
        UptimeLabel = DurationText.HumanizeUptime(snapshot.Uptime);
        IsElevated = snapshot.IsElevated;
        ElevationLabel = snapshot.IsElevated ? "Yönetici olarak çalışıyor" : "Sınırlı yetkiyle çalışıyor";

        if (snapshot.SystemDrive is { } system)
        {
            SystemDriveName = system.Name.TrimEnd('\\');
            SystemDriveFree = ByteSize.Format(system.FreeBytes);
            SystemDriveDetail = $"{ByteSize.Format(system.UsedBytes)} / {ByteSize.Format(system.TotalBytes)} dolu";
            SystemDriveUsedRatio = system.UsedRatio;
            IsSystemDriveCriticallyFull = system.IsCriticallyFull;
        }

        MemoryUsedRatio = snapshot.Memory.UsedRatio;
        MemoryLabel = ByteSize.Format(snapshot.Memory.UsedBytes);
        MemoryDetail = $"{ByteSize.Format(snapshot.Memory.TotalBytes)} toplam bellek";

        Drives.Clear();

        foreach (DriveSnapshot drive in snapshot.Drives)
        {
            Drives.Add(new DriveRow
            {
                Name = drive.Name.TrimEnd('\\'),
                Label = drive.Label,
                Detail = $"{ByteSize.Format(drive.FreeBytes)} boş · {drive.Format}",
                UsedRatio = drive.UsedRatio,
                IsCriticallyFull = drive.IsCriticallyFull
            });
        }
    }

}

public sealed class DriveRow
{
    public required string Name { get; init; }

    public required string Label { get; init; }

    public required string Detail { get; init; }

    public required double UsedRatio { get; init; }

    public required bool IsCriticallyFull { get; init; }

    public double UsedPercent => Math.Round(UsedRatio * 100);
}
