using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysScrub.Core.Drivers;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;

namespace SysScrub.App.ViewModels;

/// <summary>Listede gösterilen tek bir cihaz.</summary>
public sealed record DeviceRowViewModel(DeviceInfo Device)
{
    public string Name => Device.Name;

    public string VersionLabel => Device.DriverVersion ?? "sürüm yok";

    public string DateLabel => Device.DriverDate?.ToString("dd.MM.yyyy") ?? "tarih yok";

    public string ProviderLabel => Device.DriverProvider ?? Device.Manufacturer ?? "sağlayıcı bilinmiyor";

    public bool HasProblem => Device.HasProblem;

    public string ProblemDescription => Device.ProblemDescription;

    public bool IsUnsigned => !Device.IsSigned && !Device.HasProblem;

    public string AgeLabel
    {
        get
        {
            if (Device.DriverAge is not { } age)
            {
                return string.Empty;
            }

            int years = (int)(age.TotalDays / 365);

            return years >= 2 ? $"{years} yıl eski" : string.Empty;
        }
    }

    public bool IsAging => AgeLabel.Length > 0;
}

/// <summary>Cihaz sınıfına göre grup.</summary>
public sealed partial class DeviceGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    public DeviceGroupViewModel(DeviceGroup group)
    {
        Title = group.DisplayName;
        Devices = new ObservableCollection<DeviceRowViewModel>(
            group.Devices.Select(d => new DeviceRowViewModel(d)));
        ProblemCount = group.ProblemCount;

        // Sorunlu cihazı olan grup açık gelir: kullanıcının önce görmesi gereken o.
        _isExpanded = ProblemCount > 0;
    }

    public string Title { get; }

    public ObservableCollection<DeviceRowViewModel> Devices { get; }

    public int ProblemCount { get; }

    public bool HasProblems => ProblemCount > 0;

    public string CountLabel => $"{Devices.Count}";

    public string ProblemLabel => ProblemCount > 0 ? $"{ProblemCount} sorunlu" : string.Empty;
}

/// <summary>Windows Update'in sunduğu bir güncelleme.</summary>
public sealed record DriverUpdateRowViewModel(DriverUpdate Update)
{
    public string Title => Update.Title;

    public string Detail
    {
        get
        {
            var parts = new List<string>();

            if (Update.Manufacturer is not null)
            {
                parts.Add(Update.Manufacturer);
            }

            if (Update.Date is { } date)
            {
                parts.Add(date.ToString("dd.MM.yyyy"));
            }

            if (Update.SizeBytes > 0)
            {
                parts.Add(ByteSize.Format(Update.SizeBytes));
            }

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Sürücüler ekranı.
///
/// Güncelleme kaynağı yalnızca Windows Update: oradan gelen her sürücü WHQL imzalı
/// ve Microsoft tarafından o donanıma uygun bulunmuş. Üçüncü parti sürücü aynası
/// tutmuyoruz — DriverBooster tarzı uygulamaları güvenilmez yapan şey tam olarak o.
/// </summary>
public sealed partial class DriversViewModel : ObservableObject
{
    private readonly DeviceInventory _inventory;
    private readonly WindowsUpdateDriverSource _updateSource;
    private readonly DriverBackup _backup;
    private readonly ILogger<DriversViewModel> _logger;

    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyTitle = string.Empty;

    [ObservableProperty]
    private string _busyDetail = string.Empty;

    [ObservableProperty]
    private int _deviceCount;

    [ObservableProperty]
    private int _problemCount;

    [ObservableProperty]
    private int _agingCount;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _updateMessage = string.Empty;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private bool _hasLoaded;

    public DriversViewModel(
        DeviceInventory inventory,
        WindowsUpdateDriverSource updateSource,
        DriverBackup backup,
        SystemInfoService systemInfo,
        ILogger<DriversViewModel> logger)
    {
        _inventory = inventory;
        _updateSource = updateSource;
        _backup = backup;
        _logger = logger;

        IsElevated = systemInfo.Capture().IsElevated;

        Groups = [];
        ProblemDevices = [];
        AgingDevices = [];
        Updates = [];
    }

    public ObservableCollection<DeviceGroupViewModel> Groups { get; }

    public ObservableCollection<DeviceRowViewModel> ProblemDevices { get; }

    public ObservableCollection<DeviceRowViewModel> AgingDevices { get; }

    public ObservableCollection<DriverUpdateRowViewModel> Updates { get; }

    public bool HasProblems => ProblemCount > 0;

    public bool HasAging => AgingCount > 0;

    public bool HasUpdates => Updates.Count > 0;

    public string DeviceCountLabel => DeviceCount == 0 ? "—" : $"{DeviceCount:N0}";

    public string ProblemCountLabel => ProblemCount == 0 ? "yok" : $"{ProblemCount:N0}";

    // ------------------------------------------------------------------ envanter

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task LoadAsync()
    {
        IsBusy = true;
        BusyTitle = "Donanım okunuyor";
        BusyDetail = "cihazlar ve sürücü bilgileri toplanıyor...";
        StatusMessage = string.Empty;

        _cancellation = new CancellationTokenSource();

        try
        {
            DeviceInventoryReport report = await _inventory.LoadAsync(_cancellation.Token);

            Groups.Clear();

            foreach (DeviceGroup group in report.GroupByClass())
            {
                Groups.Add(new DeviceGroupViewModel(group));
            }

            ProblemDevices.Clear();

            foreach (DeviceInfo device in report.ProblemDevices)
            {
                ProblemDevices.Add(new DeviceRowViewModel(device));
            }

            AgingDevices.Clear();

            foreach (DeviceInfo device in report.AgingDrivers)
            {
                AgingDevices.Add(new DeviceRowViewModel(device));
            }

            DeviceCount = report.Devices.Count;
            ProblemCount = report.ProblemDevices.Count;
            AgingCount = report.AgingDrivers.Count;
            HasLoaded = true;

            StatusMessage = ProblemCount > 0
                ? $"{ProblemCount} cihaz sorun bildiriyor. Aşağıda ne olduğu yazıyor."
                : $"{DeviceCount:N0} cihaz okundu, hepsi sorunsuz çalışıyor " +
                  $"({report.Duration.TotalSeconds:F1} saniye).";

            RaiseDerived();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Okuma iptal edildi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Donanım envanteri okunamadı");
            StatusMessage = $"Donanım okunamadı: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
            RefreshCommands();
        }
    }

    // ------------------------------------------------------------------ güncelleme araması

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        BusyTitle = "Güncelleme aranıyor";
        BusyDetail = "Windows Update sorgulanıyor, bu bir dakikaya kadar sürebilir...";
        UpdateMessage = string.Empty;

        _cancellation = new CancellationTokenSource();

        try
        {
            DriverSearchResult result = await _updateSource.SearchAsync(_cancellation.Token);

            Updates.Clear();

            foreach (DriverUpdate update in result.Updates)
            {
                Updates.Add(new DriverUpdateRowViewModel(update));
            }

            UpdateMessage = result.Describe();
            RaiseDerived();
        }
        catch (OperationCanceledException)
        {
            UpdateMessage = "Arama iptal edildi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sürücü güncellemesi aranamadı");
            UpdateMessage = $"Arama başarısız: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
            RefreshCommands();
        }
    }

    // ------------------------------------------------------------------ yedekleme

    [RelayCommand(CanExecute = nameof(CanBackup))]
    private async Task BackupAsync()
    {
        IsBusy = true;
        BusyTitle = "Sürücüler yedekleniyor";
        BusyDetail = "üçüncü parti sürücü paketleri dışa aktarılıyor...";

        _cancellation = new CancellationTokenSource();

        try
        {
            DriverBackupResult result = await _backup.ExportAllAsync(_cancellation.Token);

            StatusMessage = result.Succeeded
                ? $"{result.Describe()} Klasör: {result.Path}"
                : result.Describe();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Yedekleme iptal edildi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sürücü yedeği alınamadı");
            StatusMessage = $"Yedekleme başarısız: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
            RefreshCommands();
        }
    }

    private bool CanRun() => !IsBusy;

    /// <summary>Yedekleme DriverStore'a eriştiği için yönetici hakkı ister.</summary>
    private bool CanBackup() => !IsBusy && IsElevated;

    [RelayCommand]
    private void Cancel()
    {
        _cancellation?.Cancel();
        BusyDetail = "iptal ediliyor...";
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasAging));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(DeviceCountLabel));
        OnPropertyChanged(nameof(ProblemCountLabel));
    }

    private void RefreshCommands()
    {
        LoadCommand.NotifyCanExecuteChanged();
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        BackupCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommands();
}
