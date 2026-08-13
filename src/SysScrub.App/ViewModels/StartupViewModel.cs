using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysScrub.App.Localization;
using static SysScrub.App.Localization.L;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;
using SysScrub.Core.Startup;

namespace SysScrub.App.ViewModels;

/// <summary>Listedeki tek bir başlangıç öğesi.</summary>
public sealed partial class StartupRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    public StartupRowViewModel(StartupEntry entry) => Entry = entry;

    [ObservableProperty]
    private StartupEntry _entry;

    public string Name => Entry.Name;

    public string Command => Entry.Command;

    public string SourceLabel => Entry.SourceLabel;

    public bool IsEnabled => Entry.IsEnabled;

    public bool IsToggleable => Entry.Control == StartupControl.Toggleable;

    public bool TargetMissing => Entry.TargetMissing;

    public bool IsMachineWide => Entry.IsMachineWide;

    public bool HasResult => ResultMessage.Length > 0;

    /// <summary>Ölçüm varsa "1,2 sn", yoksa boş — uydurma değer gösterilmez.</summary>
    public string DelayLabel => Entry.BootDelayMs is { } ms ? DurationText.FromMilliseconds(ms) : string.Empty;

    public bool HasDelay => Entry.BootDelayMs is > 0;

    public string ImpactLabel => Entry.ImpactLabel;

    /// <summary>Etki rozetinin rengi; eşikler <see cref="StartupEntry.ImpactLabel"/> ile aynı.</summary>
    public string ImpactSeverity => Entry.BootDelayMs switch
    {
        null => "none",
        < 300 => "good",
        < 1000 => "caution",
        _ => "danger"
    };

    public string ActionLabel => T(IsEnabled ? "St_Disable" : "St_Enable");

    public string IconKey => Entry.Source switch
    {
        StartupSource.ScheduledTask => "IconTimeline",
        StartupSource.Service => "IconSettings",
        StartupSource.StartupFolder => "IconPrograms",
        _ => "IconStartup"
    };

    /// <summary>Neden değiştirilemediğinin açıklaması; salt okunur satırlarda gösterilir.</summary>
    public string ReadOnlyReason => Entry.Source == StartupSource.Service
        ? T("St_ServiceReadOnly")
        : string.Empty;

    /// <summary>Kaydın durumu değiştikten sonra satırı tazeler.</summary>
    public void Apply(StartupEntry updated)
    {
        Entry = updated;

        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(ActionLabel));
    }

    partial void OnResultMessageChanged(string value) => OnPropertyChanged(nameof(HasResult));
}

/// <summary>
/// Başlangıç yöneticisi.
///
/// Devre dışı bırakma kaydı silmez: Windows'un kendi onay mekanizması kullanılır,
/// böylece işlem geri alınabilir ve Görev Yöneticisi ile aynı durumu gösteririz.
///
/// Etki sütunu tahmin değil — Windows'un Tanılama-Performans günlüğünden okunan
/// gerçek gecikme. Günlük okunamıyorsa sütun boş kalır.
/// </summary>
public sealed partial class StartupViewModel : ObservableObject
{
    private readonly StartupInventory _inventory;
    private readonly StartupManager _manager;
    private readonly ILogger<StartupViewModel> _logger;

    private CancellationTokenSource? _cancellation;
    private StartupInventoryReport _report = StartupInventoryReport.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyTitle = string.Empty;

    [ObservableProperty]
    private string _busyDetail = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasLoaded;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private bool _showDisabled;

    public StartupViewModel(
        StartupInventory inventory,
        StartupManager manager,
        SystemInfoService systemInfo,
        ILogger<StartupViewModel> logger)
    {
        _inventory = inventory;
        _manager = manager;
        _logger = logger;

        // Dil değişince tüm metinler yeniden okunmalı; boş ad her bağlamayı tazeliyor.
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);

        IsElevated = systemInfo.Capture().IsElevated;

        Enabled = [];
        Disabled = [];
    }

    /// <summary>Açılışta çalışan öğeler; ekranın ana listesi.</summary>
    public ObservableCollection<StartupRowViewModel> Enabled { get; }

    /// <summary>Kapatılmış öğeler; katlanmış hâlde duran alt liste.</summary>
    public ObservableCollection<StartupRowViewModel> Disabled { get; }

    public int EnabledCount => Enabled.Count;

    public int DisabledCount => Disabled.Count;

    public bool HasEntries => Enabled.Count > 0 || Disabled.Count > 0;

    public bool HasDisabled => Disabled.Count > 0;

    public string DisabledHeader => T("St_DisabledHeader", DisabledCount);

    /// <summary>Hedefi kaybolmuş öğeler; açılışta boşuna aranıyorlar.</summary>
    public int BrokenCount => Enabled.Count(r => r.TargetMissing);

    public bool HasBroken => BrokenCount > 0;

    public string BrokenMessage => BrokenCount == 1
        ? T("St_Broken1")
        : T("St_BrokenN", BrokenCount);

    public bool BootMeasurementsAvailable => _report.BootMeasurementsAvailable;

    /// <summary>Ölçülen toplam gecikme; ölçüm yoksa gösterilmez.</summary>
    public string TotalDelayLabel => _report.TotalDelayMs > 0
        ? DurationText.FromMilliseconds(_report.TotalDelayMs)
        : string.Empty;

    public bool HasTotalDelay => _report.TotalDelayMs > 0;

    public string HeadlineText
    {
        get
        {
            if (!HasLoaded)
            {
                return T("St_NotRead");
            }

            return EnabledCount == 0
                ? T("St_NoneRunning")
                : T("St_RunningCount", EnabledCount);
        }
    }

    public string HeadlineDetail
    {
        get
        {
            if (!HasLoaded)
            {
                return T("St_Intro");
            }

            if (!BootMeasurementsAvailable)
            {
                return T("St_NoMeasurement", DisabledCount);
            }

            return HasTotalDelay
                ? T("St_TotalDelay", DisabledCount, TotalDelayLabel)
                : T("St_NoDelayYet", DisabledCount);
        }
    }

    // ------------------------------------------------------------------ okuma

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task LoadAsync()
    {
        IsBusy = true;
        BusyTitle = T("St_Busy");
        BusyDetail = T("St_BusyDetail");
        StatusMessage = string.Empty;

        _cancellation = new CancellationTokenSource();

        try
        {
            _report = await _inventory.LoadAsync(_cancellation.Token);

            Rebuild();
            HasLoaded = true;

            StatusMessage = _report.BootMeasurementsAvailable
                ? T("St_ReadOk", _report.Entries.Count)
                : T("St_ReadNoLog", _report.Entries.Count);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = T("Msg_Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Başlangıç envanteri okunamadı");
            StatusMessage = T("Err_ReadFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            Finish();
        }
    }

    private bool CanRun() => !IsBusy;

    // ------------------------------------------------------------------ açma/kapama

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ToggleAsync(StartupRowViewModel row)
    {
        if (row is null || !row.IsToggleable)
        {
            return;
        }

        bool target = !row.IsEnabled;

        row.IsBusy = true;
        row.ResultMessage = string.Empty;

        try
        {
            StartupChangeResult result = await _manager.SetEnabledAsync(row.Entry, target);

            if (result.Success)
            {
                row.Apply(row.Entry with { IsEnabled = target });

                Move(row, target);

                StatusMessage = T(target ? "St_Enabled" : "St_Disabled", row.Name);
            }
            else
            {
                row.ResultMessage = result.Message ?? T("St_ChangeFailed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Başlangıç öğesi değiştirilemedi: {Name}", row.Name);
            row.ResultMessage = T("St_ChangeFailedWhy", ex.Message);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    /// <summary>Satırı açık/kapalı listeleri arasında taşır; liste yeniden yüklenmeden güncel kalır.</summary>
    private void Move(StartupRowViewModel row, bool enabled)
    {
        ObservableCollection<StartupRowViewModel> from = enabled ? Disabled : Enabled;
        ObservableCollection<StartupRowViewModel> to = enabled ? Enabled : Disabled;

        from.Remove(row);
        to.Insert(0, row);

        RaiseDerived();
    }

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private void ToggleDisabledList() => ShowDisabled = !ShowDisabled;

    private void Rebuild()
    {
        Enabled.Clear();
        Disabled.Clear();

        foreach (StartupEntry entry in _report.Entries)
        {
            var row = new StartupRowViewModel(entry);

            if (entry.IsEnabled)
            {
                Enabled.Add(row);
            }
            else
            {
                Disabled.Add(row);
            }
        }

        RaiseDerived();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(DisabledCount));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasDisabled));
        OnPropertyChanged(nameof(DisabledHeader));
        OnPropertyChanged(nameof(BrokenCount));
        OnPropertyChanged(nameof(HasBroken));
        OnPropertyChanged(nameof(BrokenMessage));
        OnPropertyChanged(nameof(BootMeasurementsAvailable));
        OnPropertyChanged(nameof(TotalDelayLabel));
        OnPropertyChanged(nameof(HasTotalDelay));
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(HeadlineDetail));
    }

    private void Finish()
    {
        _cancellation?.Dispose();
        _cancellation = null;

        LoadCommand.NotifyCanExecuteChanged();
        ToggleCommand.NotifyCanExecuteChanged();

        RaiseDerived();
    }

    partial void OnIsBusyChanged(bool value) => Finish();
}
