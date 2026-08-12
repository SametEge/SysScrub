using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;

namespace SysScrub.App.ViewModels;

/// <summary>
/// Panel. Faz 0'da yalnızca gerçekten okuyabildiğimiz veriyi gösterir —
/// tarama, sürücü ve disk sağlığı sayıları uydurulmaz, "henüz ölçülmedi" olarak durur.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly SystemInfoService _systemInfo;

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

    public DashboardViewModel(SystemInfoService systemInfo)
    {
        _systemInfo = systemInfo;
        Drives = [];
        Refresh();
    }

    public ObservableCollection<DriveRow> Drives { get; }

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
