using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysScrub.Core.Drivers;
using SysScrub.Core.Machine;

namespace SysScrub.App.ViewModels;

/// <summary>
/// Listede tek bir sürücü satırı: kategori, cihaz adı, kurulu sürüm, kullanılabilir sürüm.
/// </summary>
public sealed partial class DriverRowViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;

    [ObservableProperty]
    private bool _isSelected;

    public DriverRowViewModel(DriverStatusRow row, Action onSelectionChanged)
    {
        Row = row;
        _onSelectionChanged = onSelectionChanged;

        // Kesin güncellemeler işaretli gelir; belirsiz olanlar kullanıcı kararına bırakılır.
        _isSelected = row.Status == DriverStatus.UpdateAvailable;
    }

    public DriverStatusRow Row { get; }

    /// <summary>"Görüntü bağdaştırıcıları" gibi kategori başlığı.</summary>
    public string CategoryName => DeviceClassNames.Describe(Row.Device.DeviceClass);

    public string DeviceName => Row.Device.Name;

    public string Vendor => Row.Device.DriverProvider ?? Row.Device.Manufacturer ?? string.Empty;

    public string InstalledLabel => Row.InstalledLabel;

    public string AvailableLabel => Row.AvailableLabel;

    public string AgeLabel => Row.AgeLabel;

    public bool ShowAge => AgeLabel.Length > 0;

    public bool HasUpdate => Row.Status == DriverStatus.UpdateAvailable;

    public bool IsPossiblyOutdated => Row.Status == DriverStatus.PossiblyOutdated;

    public bool HasProblem => Row.Device.HasProblem;

    public string ProblemDescription => Row.Device.ProblemDescription;

    public string IconKey => Row.Device.DeviceClass?.ToUpperInvariant() switch
    {
        "DISPLAY" => "IconDashboard",
        "NET" or "NETTRANS" or "NETCLIENT" => "IconUpdates",
        "DISKDRIVE" or "SCSIADAPTER" or "VOLUME" or "HDC" => "IconDiskHealth",
        "USB" or "USBDEVICE" or "PRINTER" or "PRINTQUEUE" => "IconPrograms",
        _ => "IconDrivers"
    };

    /// <summary>
    /// "Kullanılabilir" satırının açıklaması. Kesin güncelleme yoksa neden
    /// bilmediğimizi ipucunda söylüyoruz; kırpılmış metin yerine tam cümle burada.
    /// </summary>
    public string AvailableTooltip => Row.Status switch
    {
        DriverStatus.UpdateAvailable =>
            "Windows Update bu sürücünün yenisini sunuyor.",
        DriverStatus.PossiblyOutdated =>
            "Windows Update yenisini sunmuyor. Sürücü eski ama üreticinin sitesinde " +
            "daha yenisi olabilir; Microsoft Update Catalog ve üretici sorgusu bir sonraki adımda geliyor.",
        _ => "Bilinen bir kaynakta daha yenisi yok."
    };

    /// <summary>Durum rozeti metni.</summary>
    public string StatusLabel => Row.Status switch
    {
        DriverStatus.UpdateAvailable => "güncelleme var",
        DriverStatus.PossiblyOutdated => "eski olabilir",
        _ => string.Empty
    };

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();
}

/// <summary>
/// Sürücüler ekranı.
///
/// Liste iki kesin gruba ayrılır: Windows Update'in yenisini sunduğu sürücüler
/// ("güncelleme var") ve iki yıldan eski olup hiçbir kaynağın yenisini sunmadığı
/// sürücüler ("eski olabilir"). İkincisine "güncel değil" demiyoruz çünkü
/// bilmiyoruz — üreticide yenisi olabilir de olmayabilir de.
/// </summary>
public sealed partial class DriversViewModel : ObservableObject
{
    private readonly DeviceInventory _inventory;
    private readonly WindowsUpdateDriverSource _updateSource;
    private readonly DriverBackup _backup;
    private readonly ILogger<DriversViewModel> _logger;

    private CancellationTokenSource? _cancellation;
    private DeviceInventoryReport _lastInventory = DeviceInventoryReport.Empty;
    private DriverSearchResult? _lastSearch;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyTitle = string.Empty;

    [ObservableProperty]
    private string _busyDetail = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _sourceMessage = string.Empty;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private bool _hasLoaded;

    [ObservableProperty]
    private bool _showUpToDate;

    [ObservableProperty]
    private int _selectedCount;

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

        Attention = [];
        UpToDate = [];
    }

    /// <summary>Güncellemesi olan ve eski olabilecek sürücüler; ekranın üst listesi.</summary>
    public ObservableCollection<DriverRowViewModel> Attention { get; }

    /// <summary>Güncel sürücüler; katlanmış hâlde duran alt liste.</summary>
    public ObservableCollection<DriverRowViewModel> UpToDate { get; }

    public int AttentionCount => Attention.Count;

    public int UpToDateCount => UpToDate.Count;

    public int UpdateAvailableCount => Attention.Count(r => r.HasUpdate);

    public bool HasAttention => Attention.Count > 0;

    public bool CanUpdateSelected => SelectedCount > 0 && Attention.Any(r => r is { IsSelected: true, HasUpdate: true });

    /// <summary>Ekranın en üstündeki tek cümlelik özet.</summary>
    public string HeadlineText
    {
        get
        {
            if (!HasLoaded)
            {
                return "Donanım henüz okunmadı";
            }

            if (AttentionCount == 0)
            {
                return "Tüm sürücüler güncel";
            }

            int updates = UpdateAvailableCount;

            return updates > 0
                ? $"{updates} aygıt sürücüsü güncel değil"
                : $"{AttentionCount} aygıt sürücüsü eski olabilir";
        }
    }

    public string HeadlineDetail
    {
        get
        {
            if (!HasLoaded)
            {
                return string.Empty;
            }

            if (!_lastSearchWasRun)
            {
                return "Windows Update henüz sorgulanmadı — \"Güncelleme ara\" ile kesin sonucu görebilirsin.";
            }

            return AttentionCount == 0
                ? $"{UpToDateCount} cihazın hepsi güncel."
                : $"Toplam {AttentionCount} · seçili {SelectedCount}";
        }
    }

    public string UpToDateHeader => $"Güncel ({UpToDateCount})";

    /// <summary>
    /// Üst listenin başlığı. Kesin güncelleme varken "güncel değil" demek doğru;
    /// yalnızca eskiler varken aynı şeyi demek olduğundan fazlasını iddia etmek olur.
    /// </summary>
    public string AttentionHeader => UpdateAvailableCount > 0 ? "GÜNCEL DEĞİL" : "ESKİ OLABİLİR";

    private bool _lastSearchWasRun;

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
            _lastInventory = await _inventory.LoadAsync(_cancellation.Token);
            Rebuild();

            HasLoaded = true;

            StatusMessage = _lastInventory.ProblemDevices.Count > 0
                ? $"{_lastInventory.ProblemDevices.Count} cihaz sorun bildiriyor."
                : $"{_lastInventory.Devices.Count:N0} cihaz okundu " +
                  $"({_lastInventory.Duration.TotalSeconds:F1} saniye).";
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
            Finish();
        }
    }

    // ------------------------------------------------------------------ güncelleme araması

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CheckUpdatesAsync()
    {
        if (_lastInventory.Devices.Count == 0)
        {
            await LoadAsync();
        }

        IsBusy = true;
        BusyTitle = "Güncelleme aranıyor";
        BusyDetail = "Windows Update sorgulanıyor, bu bir dakikaya kadar sürebilir...";

        _cancellation = new CancellationTokenSource();

        try
        {
            _lastSearch = await _updateSource.SearchAsync(_cancellation.Token);
            _lastSearchWasRun = true;

            Rebuild();

            SourceMessage = _lastSearch.Describe();
        }
        catch (OperationCanceledException)
        {
            SourceMessage = "Arama iptal edildi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sürücü güncellemesi aranamadı");
            SourceMessage = $"Arama başarısız: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Finish();
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
            Finish();
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

    [RelayCommand]
    private void ToggleUpToDate() => ShowUpToDate = !ShowUpToDate;

    [RelayCommand]
    private void SelectAll()
    {
        foreach (DriverRowViewModel row in Attention)
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (DriverRowViewModel row in Attention)
        {
            row.IsSelected = false;
        }
    }

    // ------------------------------------------------------------------ iç işler

    private void Rebuild()
    {
        DriverStatusReport report = DriverStatusMatcher.Build(_lastInventory, _lastSearch);

        Attention.Clear();
        UpToDate.Clear();

        foreach (DriverStatusRow row in report.Rows)
        {
            var viewModel = new DriverRowViewModel(row, UpdateSelection);

            if (row.NeedsAttention)
            {
                Attention.Add(viewModel);
            }
            else
            {
                UpToDate.Add(viewModel);
            }
        }

        UpdateSelection();
        RaiseDerived();
    }

    private void UpdateSelection()
    {
        SelectedCount = Attention.Count(r => r.IsSelected);
        OnPropertyChanged(nameof(CanUpdateSelected));
        OnPropertyChanged(nameof(HeadlineDetail));
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(AttentionCount));
        OnPropertyChanged(nameof(UpToDateCount));
        OnPropertyChanged(nameof(UpdateAvailableCount));
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(HeadlineDetail));
        OnPropertyChanged(nameof(UpToDateHeader));
        OnPropertyChanged(nameof(AttentionHeader));
    }

    private void Finish()
    {
        _cancellation?.Dispose();
        _cancellation = null;

        LoadCommand.NotifyCanExecuteChanged();
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        BackupCommand.NotifyCanExecuteChanged();

        RaiseDerived();
    }

    partial void OnIsBusyChanged(bool value) => Finish();
}
