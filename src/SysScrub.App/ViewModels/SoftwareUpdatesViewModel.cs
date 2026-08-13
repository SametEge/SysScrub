using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysScrub.App.Localization;
using static SysScrub.App.Localization.L;
using SysScrub.Core.Software;

namespace SysScrub.App.ViewModels;

/// <summary>Listedeki tek bir program.</summary>
public sealed partial class SoftwareUpdateRowViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    public SoftwareUpdateRowViewModel(SoftwareUpdate update, Action onSelectionChanged)
    {
        Update = update;
        _onSelectionChanged = onSelectionChanged;
    }

    public SoftwareUpdate Update { get; }

    public string Name => Update.Name;

    public string PackageId => Update.Id;

    public string InstalledVersion => Update.InstalledVersion;

    public string AvailableVersion => Update.AvailableVersion;

    public string SourceLabel => Update.IsFromStore ? "Microsoft Store" : "winget";

    public bool IsVersionUnknown => Update.IsInstalledVersionUnknown;

    public bool HasResult => ResultMessage.Length > 0;

    /// <summary>Güncellenmiş paket yeniden seçilemez.</summary>
    public bool CanSelect => !IsDone;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();

    partial void OnResultMessageChanged(string value) => OnPropertyChanged(nameof(HasResult));

    partial void OnIsDoneChanged(bool value) => OnPropertyChanged(nameof(CanSelect));
}

/// <summary>
/// Yazılım güncelleyici. Kurulu programların yeni sürümlerini winget üzerinden bulur.
///
/// Güncelleme paket kimliğiyle yapılır, adla değil: aynı ada sahip birden çok paket
/// olabiliyor ve yanlış paketi güncellemek istenmeyen bir kurulum demek.
/// </summary>
public sealed partial class SoftwareUpdatesViewModel : ObservableObject
{
    private readonly WingetService _winget;
    private readonly ILogger<SoftwareUpdatesViewModel> _logger;

    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyTitle = string.Empty;

    [ObservableProperty]
    private string _busyDetail = string.Empty;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasChecked;

    [ObservableProperty]
    private int _selectedCount;

    public SoftwareUpdatesViewModel(WingetService winget, ILogger<SoftwareUpdatesViewModel> logger)
    {
        _winget = winget;
        _logger = logger;

        // Dil değişince tüm metinler yeniden okunmalı; boş ad her bağlamayı tazeliyor.
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);

        Updates = [];
    }

    public ObservableCollection<SoftwareUpdateRowViewModel> Updates { get; }

    public int UpdateCount => Updates.Count;

    public bool HasUpdates => Updates.Count > 0;

    public bool CanUpdateSelected => !IsBusy && SelectedCount > 0;

    public string ProgressPercentLabel => $"%{Math.Round(ProgressFraction * 100)}";

    public string HeadlineText
    {
        get
        {
            if (!HasChecked)
            {
                return T("Up_NotChecked");
            }

            return UpdateCount == 0
                ? T("Up_AllCurrent")
                : T("Up_HaveNew", UpdateCount);
        }
    }

    public string HeadlineDetail => HasChecked && UpdateCount > 0
        ? T("Up_SelectedSource", SelectedCount)
        : T("Up_Intro");

    // ------------------------------------------------------------------ denetleme

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CheckAsync()
    {
        IsBusy = true;
        BusyTitle = T("Up_Busy_Check");
        BusyDetail = T("Up_Busy_CheckDetail");
        ProgressFraction = 0;
        StatusMessage = string.Empty;

        _cancellation = new CancellationTokenSource();

        try
        {
            SoftwareUpdateList result = await _winget.ListUpgradesAsync(_cancellation.Token);

            Updates.Clear();

            foreach (SoftwareUpdate update in result.Updates.OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Updates.Add(new SoftwareUpdateRowViewModel(update, UpdateSelection));
            }

            HasChecked = true;
            StatusMessage = result.Describe();

            UpdateSelection();
            RaiseDerived();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = T("Msg_Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Program listesi alınamadı");
            StatusMessage = T("Err_CheckFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            Finish();
        }
    }

    private bool CanRun() => !IsBusy;

    // ------------------------------------------------------------------ güncelleme

    [RelayCommand(CanExecute = nameof(CanUpdateSelected))]
    private Task UpdateSelectedAsync() =>
        UpdateManyAsync(Updates.Where(u => u is { IsSelected: true, IsDone: false }).ToArray());

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UpdateOneAsync(SoftwareUpdateRowViewModel row) => UpdateManyAsync([row]);

    private async Task UpdateManyAsync(IReadOnlyList<SoftwareUpdateRowViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        IsBusy = true;
        BusyTitle = T("Up_Busy_Update");
        ProgressFraction = 0;

        _cancellation = new CancellationTokenSource();

        int succeeded = 0;
        int failed = 0;

        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                _cancellation.Token.ThrowIfCancellationRequested();

                SoftwareUpdateRowViewModel row = rows[i];

                row.IsUpdating = true;
                BusyDetail = $"{i + 1} / {rows.Count}  ·  {row.Name}";
                ProgressFraction = (double)i / rows.Count;

                SoftwareUpgradeResult result = await _winget.UpgradeAsync(row.PackageId, _cancellation.Token);

                row.IsUpdating = false;
                row.IsDone = result.Succeeded;
                row.ResultMessage = result.Describe();
                row.IsSelected = false;

                if (result.Succeeded)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                }
            }

            ProgressFraction = 1;

            StatusMessage = failed == 0
                ? T("Up_Res_Ok", succeeded)
                : T("Up_Res_Partial", succeeded, failed);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = T("Up_Res_Cancelled", succeeded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Güncelleme başarısız");
            StatusMessage = T("Err_UpdateFailed", ex.Message);
        }
        finally
        {
            foreach (SoftwareUpdateRowViewModel row in rows)
            {
                row.IsUpdating = false;
            }

            IsBusy = false;
            UpdateSelection();
            Finish();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        // winget çalışan bir kurulumu yarıda kesmiyor; iptal yalnızca sıradakileri durduruyor.
        _cancellation?.Cancel();
        BusyDetail = T("Up_Cancelling");
    }

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    private void SetAll(bool selected)
    {
        foreach (SoftwareUpdateRowViewModel row in Updates.Where(r => !r.IsDone))
        {
            row.IsSelected = selected;
        }
    }

    private void UpdateSelection()
    {
        SelectedCount = Updates.Count(r => r is { IsSelected: true, IsDone: false });

        OnPropertyChanged(nameof(CanUpdateSelected));
        OnPropertyChanged(nameof(HeadlineDetail));
        UpdateSelectedCommand.NotifyCanExecuteChanged();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(HeadlineDetail));
    }

    private void Finish()
    {
        _cancellation?.Dispose();
        _cancellation = null;

        CheckCommand.NotifyCanExecuteChanged();
        UpdateOneCommand.NotifyCanExecuteChanged();
        UpdateSelectedCommand.NotifyCanExecuteChanged();

        RaiseDerived();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUpdateSelected));
        Finish();
    }

    partial void OnProgressFractionChanged(double value) => OnPropertyChanged(nameof(ProgressPercentLabel));
}
