using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysScrub.Core.Disks;
using SysScrub.Core.Formatting;

namespace SysScrub.App.ViewModels;

/// <summary>Sağlık ekranındaki tek bir ölçüm kutusu.</summary>
public sealed record DiskMetric(string Label, string Value, string Detail, string Severity)
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>Üstteki disk seçici şeritteki tek kart.</summary>
public sealed partial class DiskCardViewModel(DiskInfo disk) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public DiskInfo Disk { get; } = disk;

    public string Model => Disk.Model;

    public string Summary => $"{Disk.CapacityLabel}  ·  {Disk.BusType}  ·  {(Disk.IsSolidState ? "SSD" : "HDD")}";

    public string StatusLabel => DiskHealthViewModel.Describe(Disk.Status);

    public string Severity => DiskHealthViewModel.SeverityOf(Disk.Status);

    public string TemperatureLabel => Disk.TemperatureCelsius is > 0 ? $"{Disk.TemperatureCelsius} °C" : string.Empty;

    public bool HasTemperature => Disk.TemperatureCelsius is > 0;
}

/// <summary>
/// Disk sağlığı ekranı.
///
/// Amaç ham veriyi anlaşılır kılmak: kullanıcı "Reallocated Sector Count:
/// 0x000000000000" değil "Bozuk sektör yok" görüyor. Ham değer bir tık uzakta,
/// tabloda duruyor.
///
/// Bilmediğimize iyi demiyoruz: S.M.A.R.T. okunamadıysa durum "bilinmiyor" kalıyor
/// ve nedeni yazılıyor. Yeşil rozet göstermek kullanıcıyı yanlış güvene sokar.
/// </summary>
public sealed partial class DiskHealthViewModel : ObservableObject
{
    private readonly DiskInventory _inventory;
    private readonly ILogger<DiskHealthViewModel> _logger;

    private CancellationTokenSource? _cancellation;
    private DiskHealthReport _report = DiskHealthReport.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasLoaded;

    [ObservableProperty]
    private DiskCardViewModel? _selected;

    [ObservableProperty]
    private bool _showAllAttributes;

    public DiskHealthViewModel(DiskInventory inventory, ILogger<DiskHealthViewModel> logger)
    {
        _inventory = inventory;
        _logger = logger;

        Disks = [];
        Metrics = [];
        Attributes = [];
    }

    public ObservableCollection<DiskCardViewModel> Disks { get; }

    /// <summary>Seçili diskin ölçüm kutuları.</summary>
    public ObservableCollection<DiskMetric> Metrics { get; }

    /// <summary>ATA disklerde tam S.M.A.R.T. tablosu.</summary>
    public ObservableCollection<SmartAttribute> Attributes { get; }

    public bool HasDisks => Disks.Count > 0;

    public bool HasAttributes => Attributes.Count > 0;

    public bool IsElevated => _report.IsElevated;

    public bool ShowElevationNotice => HasLoaded && !_report.IsElevated;

    public DiskInfo? SelectedDisk => Selected?.Disk;

    public string SelectedStatusLabel => Describe(SelectedDisk?.Status ?? DiskHealthStatus.Unknown);

    public string SelectedSeverity => SeverityOf(SelectedDisk?.Status ?? DiskHealthStatus.Unknown);

    public string SelectedReason => SelectedDisk?.StatusReason ?? string.Empty;

    public string SelectedTitle => SelectedDisk?.Model ?? string.Empty;

    public string SelectedSubtitle
    {
        get
        {
            if (SelectedDisk is not { } disk)
            {
                return string.Empty;
            }

            string[] parts = new[]
            {
                disk.CapacityLabel,
                disk.BusType,
                disk.IsSolidState ? "Katı hal (SSD)" : "Dönen disk (HDD)",
                disk.FirmwareRevision is { Length: > 0 } fw ? $"bellenim {fw}" : null
            }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray()!;

            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Halka göstergesi 0–1 arasında; ölçüm yoksa halka boş kalır.</summary>
    public double HealthFraction => (SelectedDisk?.HealthPercent ?? 0) / 100d;

    public string HealthPercentLabel => SelectedDisk?.HealthPercent is { } percent ? $"%{percent}" : "—";

    public bool HasHealthPercent => SelectedDisk?.HealthPercent is not null;

    public string AccessMessage => SelectedDisk?.AccessMessage ?? string.Empty;

    /// <summary>Yazma ömrü çubuğu: tüketilen oran.</summary>
    public double UsedLifeFraction => SelectedDisk?.Nvme is { } nvme
        ? Math.Clamp(nvme.PercentageUsed / 100d, 0, 1)
        : 0;

    public string UsedLifeLabel => SelectedDisk?.Nvme is { } nvme
        ? $"Yazma ömrünün %{nvme.PercentageUsed}'i tüketildi"
        : string.Empty;

    public bool HasUsedLife => SelectedDisk?.Nvme is not null;

    public string HeadlineText
    {
        get
        {
            if (!HasLoaded)
            {
                return "Diskler henüz okunmadı";
            }

            return Disks.Count switch
            {
                0 => "Fiziksel disk bulunamadı",
                1 => "1 disk bulundu",
                _ => $"{Disks.Count} disk bulundu"
            };
        }
    }

    public string HeadlineDetail
    {
        get
        {
            if (!HasLoaded)
            {
                return "S.M.A.R.T. verisi diskin kendisinden okunur; hiçbir şeye yazılmaz.";
            }

            return _report.ReadableCount == Disks.Count
                ? $"{_report.ReadableCount} diskin sağlık verisi okundu."
                : $"{_report.ReadableCount} / {Disks.Count} diskin sağlık verisi okunabildi.";
        }
    }

    // ------------------------------------------------------------------ okuma

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = string.Empty;

        _cancellation = new CancellationTokenSource();

        try
        {
            _report = await _inventory.LoadAsync(_cancellation.Token);

            string? previous = Selected?.Disk.Model;

            Disks.Clear();

            foreach (DiskInfo disk in _report.Disks)
            {
                Disks.Add(new DiskCardViewModel(disk));
            }

            HasLoaded = true;

            // Yeniden okumada kullanıcının baktığı disk seçili kalsın.
            Select(Disks.FirstOrDefault(d => d.Model == previous) ?? Disks.FirstOrDefault());

            StatusMessage = _report.IsElevated
                ? $"{Disks.Count} disk okundu " +
                  $"({DurationText.FromMilliseconds((int)_report.Duration.TotalMilliseconds)})."
                : "Disk kimlikleri okundu. S.M.A.R.T. verisi için uygulamanın yönetici olarak " +
                  "çalışması gerekiyor.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Okuma iptal edildi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disk sağlığı okunamadı");
            StatusMessage = $"Diskler okunamadı: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Finish();
        }
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand]
    private void SelectDisk(DiskCardViewModel card) => Select(card);

    [RelayCommand]
    private void ToggleAllAttributes() => ShowAllAttributes = !ShowAllAttributes;

    private void Select(DiskCardViewModel? card)
    {
        foreach (DiskCardViewModel disk in Disks)
        {
            disk.IsSelected = ReferenceEquals(disk, card);
        }

        Selected = card;
    }

    // ------------------------------------------------------------------ ölçüm kutuları

    private void RebuildDetails()
    {
        Metrics.Clear();
        Attributes.Clear();

        if (SelectedDisk is not { } disk)
        {
            RaiseSelectionDerived();
            return;
        }

        foreach (DiskMetric metric in BuildMetrics(disk))
        {
            Metrics.Add(metric);
        }

        foreach (SmartAttribute attribute in FilterAttributes(disk.Attributes))
        {
            Attributes.Add(attribute);
        }

        RaiseSelectionDerived();
    }

    /// <summary>
    /// Varsayılan görünüm anlamlı öznitelikleri gösteriyor: 30 satırın çoğu her
    /// diskte sabit ve kullanıcıya bir şey söylemiyor. Tamamı bir tık uzakta.
    /// </summary>
    private IEnumerable<SmartAttribute> FilterAttributes(IReadOnlyList<SmartAttribute> attributes)
    {
        if (ShowAllAttributes)
        {
            return attributes;
        }

        return attributes.Where(a => a.Status != DiskHealthStatus.Good || a.IsCritical || a.Raw > 0);
    }

    private static IEnumerable<DiskMetric> BuildMetrics(DiskInfo disk)
    {
        if (disk.TemperatureCelsius is > 0 and var temperature)
        {
            yield return new DiskMetric(
                "Sıcaklık",
                $"{temperature} °C",
                temperature >= 70 ? "Uzun vadede ömrü kısaltan seviye" : "Normal aralıkta",
                temperature >= 80 ? "danger" : temperature >= 70 ? "caution" : "good");
        }

        if (disk.PowerOnHours is > 0 and var hours)
        {
            yield return new DiskMetric(
                "Açık kalma süresi",
                DurationText.Humanize(TimeSpan.FromHours(hours)),
                $"{hours:N0} saat",
                "none");
        }

        if (disk.PowerCycles is > 0 and var cycles)
        {
            yield return new DiskMetric("Açılma sayısı", $"{cycles:N0}", string.Empty, "none");
        }

        if (disk.Nvme is not { } nvme)
        {
            yield break;
        }

        yield return new DiskMetric(
            "Yazılan toplam veri",
            ByteSize.Format(nvme.BytesWritten),
            $"okunan {ByteSize.Format(nvme.BytesRead)}",
            "none");

        yield return new DiskMetric(
            "Kalan ömür",
            $"%{Math.Clamp(100 - nvme.PercentageUsed, 0, 100)}",
            nvme.PercentageUsed >= 80 ? "Değişim planlanmalı" : "Üreticinin öngördüğü yazma ömrüne göre",
            nvme.PercentageUsed >= 100 ? "danger" : nvme.PercentageUsed >= 80 ? "caution" : "good");

        yield return new DiskMetric(
            "Kalan yedek blok",
            $"%{nvme.AvailableSpare}",
            $"üreticinin eşiği %{nvme.AvailableSpareThreshold}",
            nvme.AvailableSpare <= nvme.AvailableSpareThreshold ? "danger" : "good");

        yield return new DiskMetric(
            "Ani kapanma",
            $"{nvme.UnsafeShutdowns:N0}",
            "Elektriğin düzgün kesilmediği durum sayısı",
            "none");

        yield return new DiskMetric(
            "Düzeltilemeyen hata",
            $"{nvme.MediaErrors:N0}",
            nvme.MediaErrors > 0 ? "Veri kaybı olmuş olabilir" : "Veri bütünlüğü sorunu yok",
            nvme.MediaErrors > 0 ? "danger" : "good");

        if (nvme.SensorsCelsius.Count > 0)
        {
            yield return new DiskMetric(
                "Ek sıcaklık sensörleri",
                string.Join(" · ", nvme.SensorsCelsius.Select(s => $"{s} °C")),
                // Bileşik sıcaklık üreticiye özel bir hesap; sensörlerin en yükseği
                // olmak zorunda değil. Bu yüzden sağlık kararına katılmıyorlar.
                "Denetleyici ve bellek sensörleri; sağlık kararına katılmaz",
                "none");
        }
    }

    // ------------------------------------------------------------------ yardımcılar

    internal static string Describe(DiskHealthStatus status) => status switch
    {
        DiskHealthStatus.Good => "İyi",
        DiskHealthStatus.Caution => "Dikkat",
        DiskHealthStatus.Bad => "Kötü",
        _ => "Bilinmiyor"
    };

    internal static string SeverityOf(DiskHealthStatus status) => status switch
    {
        DiskHealthStatus.Good => "good",
        DiskHealthStatus.Caution => "caution",
        DiskHealthStatus.Bad => "danger",
        _ => "none"
    };

    private void RaiseSelectionDerived()
    {
        OnPropertyChanged(nameof(SelectedDisk));
        OnPropertyChanged(nameof(SelectedStatusLabel));
        OnPropertyChanged(nameof(SelectedSeverity));
        OnPropertyChanged(nameof(SelectedReason));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedSubtitle));
        OnPropertyChanged(nameof(HealthFraction));
        OnPropertyChanged(nameof(HealthPercentLabel));
        OnPropertyChanged(nameof(HasHealthPercent));
        OnPropertyChanged(nameof(AccessMessage));
        OnPropertyChanged(nameof(UsedLifeFraction));
        OnPropertyChanged(nameof(UsedLifeLabel));
        OnPropertyChanged(nameof(HasUsedLife));
        OnPropertyChanged(nameof(HasAttributes));
    }

    private void Finish()
    {
        _cancellation?.Dispose();
        _cancellation = null;

        LoadCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(HasDisks));
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(HeadlineDetail));
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(ShowElevationNotice));
    }

    partial void OnSelectedChanged(DiskCardViewModel? value) => RebuildDetails();

    partial void OnShowAllAttributesChanged(bool value) => RebuildDetails();

    partial void OnIsBusyChanged(bool value) => Finish();
}
