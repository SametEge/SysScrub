using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysScrub.App.Localization;
using static SysScrub.App.Localization.L;
using SysScrub.Core.Formatting;
using SysScrub.Core.Programs;

namespace SysScrub.App.ViewModels;

/// <summary>Listeyi hangi sütuna göre sıralayacağımız.</summary>
public enum ProgramSort
{
    Size,
    Name,
    Date
}

/// <summary>Listedeki tek bir program.</summary>
public sealed partial class ProgramRowViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRemoved;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    [ObservableProperty]
    private InstalledProgram _program;

    public ProgramRowViewModel(InstalledProgram program, Action onSelectionChanged)
    {
        _program = program;
        _onSelectionChanged = onSelectionChanged;
    }

    public string Name => Program.Name;

    public string Id => Program.Id;

    public bool CanUninstall => Program.CanUninstall && !IsRemoved;

    public bool IsStore => Program.Source == ProgramSource.Store;

    public bool IsSystemComponent => Program.IsSystemComponent;

    public bool UninstallerMissing => Program.UninstallerMissing;

    public string SourceLabel => Program.SourceLabel;

    public string SizeLabel => Program.HasSize ? ByteSize.Format(Program.SizeBytes) : "—";

    public bool HasSize => Program.HasSize;

    /// <summary>Ölçülmüş boyut kaydın bildirdiğinden farklı bir güven taşıyor.</summary>
    public bool IsSizeMeasured => Program.MeasuredSizeBytes is > 0;

    public string DateLabel => Program.InstallDate?.ToString("dd.MM.yyyy") ?? string.Empty;

    public bool HasDate => Program.InstallDate is not null;

    /// <summary>"Yayıncı · sürüm" — ikisi de yoksa satır boş kalır.</summary>
    public string DetailLine
    {
        get
        {
            string[] parts = new[] { Program.Publisher, Program.Version }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray()!;

            return string.Join("  ·  ", parts);
        }
    }

    public string LocationLabel => Program.InstallLocation ?? string.Empty;

    public bool HasLocation => Program.InstallLocation is { Length: > 0 };

    public bool HasResult => ResultMessage.Length > 0;

    public string ActionTooltip => Program.CanUninstall
        ? Program.SupportsQuietUninstall
            ? T("Pr_QuietTip")
            : T("Pr_OwnUninstallerTip")
        : Program.UninstallerMissing
            ? T("Pr_MissingTip")
            : T("Pr_NoCommandTip");

    public string IconKey => IsStore ? "IconUpdates" : "IconPrograms";

    public void Apply(InstalledProgram updated)
    {
        Program = updated;

        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(HasSize));
        OnPropertyChanged(nameof(IsSizeMeasured));
    }

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();

    partial void OnResultMessageChanged(string value) => OnPropertyChanged(nameof(HasResult));

    partial void OnIsRemovedChanged(bool value) => OnPropertyChanged(nameof(CanUninstall));
}

/// <summary>
/// Programlar ekranı.
///
/// Boyut sütunu iki kaynaktan geliyor: kaydın bildirdiği tahmin ve kurulum klasörü
/// taranarak ölçülen gerçek değer. Ölçüm listeyi bekletmiyor — envanter hemen
/// geliyor, boyutlar arkada dolduruyor.
///
/// Kaldırmayı programın kendi kaldırıcısı yapıyor. Biz sonucu, kaydın gerçekten
/// silinip silinmediğine bakarak doğruluyoruz; çıkış kodu güvenilir değil.
/// </summary>
public sealed partial class ProgramsViewModel : ObservableObject
{
    private readonly ProgramInventory _inventory;
    private readonly ProgramSizeCalculator _sizes;
    private readonly ProgramUninstaller _uninstaller;
    private readonly ILogger<ProgramsViewModel> _logger;

    private readonly List<ProgramRowViewModel> _all = [];

    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _sizeCancellation;
    private ProgramInventoryReport _report = ProgramInventoryReport.Empty;

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
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showComponents;

    [ObservableProperty]
    private ProgramSort _sort = ProgramSort.Size;

    [ObservableProperty]
    private bool _isMeasuring;

    [ObservableProperty]
    private int _measuredCount;

    [ObservableProperty]
    private int _selectedCount;

    // Kaldırma sonrası kalan klasör; kullanıcı onaylarsa Geri Dönüşüm Kutusu'na gider.
    [ObservableProperty]
    private string? _leftoverPath;

    [ObservableProperty]
    private long _leftoverBytes;

    public ProgramsViewModel(
        ProgramInventory inventory,
        ProgramSizeCalculator sizes,
        ProgramUninstaller uninstaller,
        ILogger<ProgramsViewModel> logger)
    {
        _inventory = inventory;
        _sizes = sizes;
        _uninstaller = uninstaller;
        _logger = logger;

        // Dil değişince tüm metinler yeniden okunmalı; boş ad her bağlamayı tazeliyor.
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);

        Programs = [];
    }

    /// <summary>Ekranda gösterilen, süzülmüş ve sıralanmış liste.</summary>
    public ObservableCollection<ProgramRowViewModel> Programs { get; }

    public int ShownCount => Programs.Count;

    public bool HasPrograms => Programs.Count > 0;

    public bool CanUninstallSelected => !IsBusy && SelectedCount > 0;

    public bool HasLeftover => LeftoverPath is not null;

    public string LeftoverMessage => LeftoverPath is null
        ? string.Empty
        : T("Pr_Leftover", ByteSize.Format(LeftoverBytes), LeftoverPath);

    public bool SortBySize => Sort == ProgramSort.Size;

    public bool SortByName => Sort == ProgramSort.Name;

    public bool SortByDate => Sort == ProgramSort.Date;

    public string HeadlineText
    {
        get
        {
            if (!HasLoaded)
            {
                return T("Pr_NotRead");
            }

            return ShownCount == ShownTotal
                ? T("Pr_Installed", ShownCount)
                : T("Pr_Showing", ShownCount, ShownTotal);
        }
    }

    public string HeadlineDetail
    {
        get
        {
            if (!HasLoaded)
            {
                return T("Pr_Source");
            }

            if (IsMeasuring)
            {
                return T("Pr_Measuring", MeasuredCount);
            }

            string total = KnownSize > 0 ? T("Pr_KnownTotal", ByteSize.Format(KnownSize)) : T("Pr_NotMeasured");

            return SelectedCount > 0
                ? T("Pr_SelectedTotal", SelectedCount, total)
                : T("Pr_StoreTotal", _report.StoreCount, total);
        }
    }

    private int ShownTotal => _all.Count(r => ShowComponents || !r.IsSystemComponent);

    private long KnownSize => _all
        .Where(r => ShowComponents || !r.IsSystemComponent)
        .Sum(r => r.Program.SizeBytes);

    // ------------------------------------------------------------------ okuma

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task LoadAsync()
    {
        IsBusy = true;
        BusyTitle = T("Pr_Busy_Read");
        BusyDetail = T("Pr_Busy_ReadDetail");
        StatusMessage = string.Empty;
        LeftoverPath = null;

        _cancellation = new CancellationTokenSource();

        try
        {
            _report = await _inventory.LoadAsync(_cancellation.Token);

            _all.Clear();

            foreach (InstalledProgram program in _report.Programs)
            {
                _all.Add(new ProgramRowViewModel(program, UpdateSelection));
            }

            HasLoaded = true;
            ApplyFilter();

            StatusMessage = T(
                "Pr_ReadResult",
                _report.VisibleCount,
                _report.StoreCount,
                _report.ComponentCount,
                DurationText.FromMilliseconds((int)_report.Duration.TotalMilliseconds));

            // Ölçüm listeyi bekletmiyor; satırlar dolarken kullanıcı listeyi kullanabiliyor.
            StartMeasuring();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = T("Msg_Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Program envanteri okunamadı");
            StatusMessage = T("Err_ReadFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            Finish();
        }
    }

    private bool CanRun() => !IsBusy;

    // ------------------------------------------------------------------ boyut ölçümü

    private void StartMeasuring()
    {
        _sizeCancellation?.Cancel();
        _sizeCancellation = new CancellationTokenSource();

        IsMeasuring = true;
        MeasuredCount = 0;

        var lookup = _all.ToDictionary(r => r.Id, StringComparer.Ordinal);

        // Progress arayüz iş parçacığında oluşturuluyor; geri çağrılar da orada çalışıyor.
        var progress = new Progress<ProgramSize>(size =>
        {
            if (!lookup.TryGetValue(size.ProgramId, out ProgramRowViewModel? row))
            {
                return;
            }

            row.Apply(row.Program with { MeasuredSizeBytes = size.Bytes });
            MeasuredCount++;

            OnPropertyChanged(nameof(HeadlineDetail));
        });

        InstalledProgram[] targets = _all.Select(r => r.Program).ToArray();
        CancellationToken token = _sizeCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await _sizes.MeasureAsync(targets, progress, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Boyut ölçümü tamamlanamadı");
            }
        }, token).ContinueWith(_ => FinishMeasuring(), TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void FinishMeasuring()
    {
        IsMeasuring = false;

        // Ölçüm bittiğinde boyut sıralaması artık gerçek rakamlara dayanıyor.
        if (Sort == ProgramSort.Size)
        {
            ApplyFilter();
        }

        RaiseDerived();
    }

    // ------------------------------------------------------------------ kaldırma

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UninstallAsync(ProgramRowViewModel row) => UninstallManyAsync([row]);

    [RelayCommand(CanExecute = nameof(CanUninstallSelected))]
    private Task UninstallSelectedAsync() =>
        UninstallManyAsync(Programs.Where(r => r is { IsSelected: true, CanUninstall: true }).ToArray());

    private async Task UninstallManyAsync(IReadOnlyList<ProgramRowViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        IsBusy = true;
        BusyTitle = T(rows.Count == 1 ? "Pr_Busy_Uninstall1" : "Pr_Busy_UninstallN");
        LeftoverPath = null;

        _cancellation = new CancellationTokenSource();

        int removed = 0;
        int failed = 0;

        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                _cancellation.Token.ThrowIfCancellationRequested();

                ProgramRowViewModel row = rows[i];

                row.IsBusy = true;
                BusyDetail = rows.Count == 1
                    ? row.Name
                    : $"{i + 1} / {rows.Count}  ·  {row.Name}";

                UninstallResult result = await _uninstaller.UninstallAsync(
                    row.Program, preferQuiet: true, _cancellation.Token);

                row.IsBusy = false;
                row.IsRemoved = result.Succeeded;
                row.IsSelected = false;
                row.ResultMessage = result.Succeeded ? string.Empty : result.Describe();

                if (result.Succeeded)
                {
                    removed++;

                    if (result.HasLeftover)
                    {
                        LeftoverPath = result.LeftoverDirectory;
                        LeftoverBytes = result.LeftoverBytes;
                    }
                }
                else
                {
                    failed++;
                }
            }

            StatusMessage = failed == 0
                ? T("Pr_Res_Ok", removed)
                : T("Pr_Res_Partial", removed, failed);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = T("Pr_Res_Cancelled", removed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kaldırma başarısız");
            StatusMessage = T("Err_UninstallFailed", ex.Message);
        }
        finally
        {
            foreach (ProgramRowViewModel row in rows)
            {
                row.IsBusy = false;
            }

            IsBusy = false;
            UpdateSelection();
            Finish();
        }
    }

    [RelayCommand]
    private async Task RemoveLeftoverAsync()
    {
        if (LeftoverPath is not { Length: > 0 } path)
        {
            return;
        }

        bool removed = await _uninstaller.RemoveLeftoverAsync(path);

        StatusMessage = removed
            ? T("Pr_LeftoverMoved", path)
            : T("Pr_LeftoverFailed", path);

        LeftoverPath = null;
    }

    [RelayCommand]
    private void DismissLeftover() => LeftoverPath = null;

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    // ------------------------------------------------------------------ süzme ve sıralama

    [RelayCommand]
    private void SortBy(string sort)
    {
        Sort = sort switch
        {
            "name" => ProgramSort.Name,
            "date" => ProgramSort.Date,
            _ => ProgramSort.Size
        };
    }

    [RelayCommand]
    private void ToggleComponents() => ShowComponents = !ShowComponents;

    private void ApplyFilter()
    {
        string search = SearchText.Trim();

        IEnumerable<ProgramRowViewModel> query = _all
            .Where(r => ShowComponents || !r.IsSystemComponent)
            .Where(r => search.Length == 0 ||
                        r.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                        (r.Program.Publisher?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false));

        query = Sort switch
        {
            ProgramSort.Name => query.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            // Tarihi olmayan programlar sona: "bilinmiyor" en yeni gibi görünmemeli.
            ProgramSort.Date => query
                .OrderByDescending(r => r.Program.InstallDate ?? DateTime.MinValue)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query
                .OrderByDescending(r => r.Program.SizeBytes)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        Programs.Clear();

        foreach (ProgramRowViewModel row in query)
        {
            Programs.Add(row);
        }

        UpdateSelection();
        RaiseDerived();
    }

    private void UpdateSelection()
    {
        SelectedCount = Programs.Count(r => r is { IsSelected: true, CanUninstall: true });

        OnPropertyChanged(nameof(CanUninstallSelected));
        OnPropertyChanged(nameof(HeadlineDetail));
        UninstallSelectedCommand.NotifyCanExecuteChanged();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(ShownCount));
        OnPropertyChanged(nameof(HasPrograms));
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(HeadlineDetail));
        OnPropertyChanged(nameof(SortBySize));
        OnPropertyChanged(nameof(SortByName));
        OnPropertyChanged(nameof(SortByDate));
    }

    private void Finish()
    {
        _cancellation?.Dispose();
        _cancellation = null;

        LoadCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
        UninstallSelectedCommand.NotifyCanExecuteChanged();

        RaiseDerived();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnShowComponentsChanged(bool value) => ApplyFilter();

    partial void OnSortChanged(ProgramSort value) => ApplyFilter();

    partial void OnIsBusyChanged(bool value) => Finish();

    partial void OnLeftoverPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasLeftover));
        OnPropertyChanged(nameof(LeftoverMessage));
    }

    partial void OnIsMeasuringChanged(bool value) => OnPropertyChanged(nameof(HeadlineDetail));
}
