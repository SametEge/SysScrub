using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using static SysScrub.App.Localization.L;
using SysScrub.App.Localization;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;
using SysScrub.Core.RegistryCleaning;
using SysScrub.Core.Rules;
using SysScrub.Core.Settings;

namespace SysScrub.App.ViewModels;

/// <summary>Listede gösterilen tek bir ölü kayıt.</summary>
public sealed record RegistryFindingViewModel(string Path, string Reason, string Target);

/// <summary>Ağaçtaki bir tarayıcı satırı.</summary>
public sealed partial class RegistryScannerNodeViewModel : ObservableObject
{
    private const int MaxShownFindings = 150;

    private readonly Action _onSelectionChanged;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _wasScanned;

    [ObservableProperty]
    private IReadOnlyList<RegistryFindingViewModel> _findings = [];

    public RegistryScannerNodeViewModel(IRegistryScanner scanner, bool isElevated, Action onSelectionChanged)
    {
        Scanner = scanner;
        IsBlockedByElevation = scanner.RequiresAdmin && !isElevated;
        _onSelectionChanged = onSelectionChanged;
        _isSelected = scanner.DefaultEnabled;
    }

    public IRegistryScanner Scanner { get; }

    public string Title => Scanner.Title;

    public string Explanation => Scanner.Explanation;

    public RiskLevel Risk => Scanner.Risk;

    public bool ShowRiskBadge => Risk != RiskLevel.Safe;

    public string RiskLabel => T(Risk == RiskLevel.Caution ? "Risk_Caution" : "Risk_Advanced");

    public bool RequiresAdmin => Scanner.RequiresAdmin;

    public bool IsBlockedByElevation { get; }

    public string ElevationNote => IsBlockedByElevation
        ? LocalizationService.Instance["Msg_NeedsAdmin"]
        : string.Empty;

    public string CountLabel => !WasScanned
        ? string.Empty
        : Count > 0 ? T("Rg_Records", $"{Count:N0}") : T("Rg_Clean");

    public bool IsEmpty => WasScanned && Count == 0;

    public bool HasFindings => Count > 0;

    public string TruncationNote => Count > MaxShownFindings
        ? T("Rg_Truncated", $"{MaxShownFindings:N0}", $"{Count:N0}")
        : string.Empty;

    public void Apply(RegistryScannerResult result)
    {
        Count = result.Count;
        WasScanned = true;

        Findings = result.Findings
            .Take(MaxShownFindings)
            .Select(f => new RegistryFindingViewModel(
                f.Location.DisplayPath,
                f.Reason,
                f.Target ?? string.Empty))
            .ToArray();

        RaiseDerived();
    }

    public void Reset()
    {
        Count = 0;
        WasScanned = false;
        Findings = [];
        IsExpanded = false;

        RaiseDerived();
    }

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(TruncationNote));
    }
}

/// <summary>
/// Registry ekranı. Temizleyiciyle aynı akış: tara → incele → temizle.
///
/// Fark, güvenlik ağının ağırlığında: her silme öncesi .reg yedeği alınır ve
/// sistem geri yükleme noktası oluşturulur. Yedek alınamazsa hiçbir şey silinmez.
/// </summary>
public sealed partial class RegistryViewModel : ObservableObject
{
    private readonly RegistryScanEngine _scanner;
    private readonly RegistryCleanEngine _cleaner;
    private readonly HistoryStore _history;
    private readonly SettingsStore _settings;
    private readonly ILogger<RegistryViewModel> _logger;

    private CancellationTokenSource? _cancellation;
    private RegistryScanReport _lastReport = RegistryScanReport.Empty;
    private RegistryCleanResult? _lastClean;

    [ObservableProperty]
    private CleanerStage _stage = CleanerStage.Ready;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string _progressLabel = string.Empty;

    [ObservableProperty]
    private string _busyTitle = string.Empty;

    [ObservableProperty]
    private string _busyDetail = string.Empty;

    [ObservableProperty]
    private int _foundCount;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    [ObservableProperty]
    private string _resultDetail = string.Empty;

    [ObservableProperty]
    private bool _canUndoLastClean;

    [ObservableProperty]
    private bool _isElevated;

    public RegistryViewModel(
        RegistryScanEngine scanner,
        RegistryCleanEngine cleaner,
        HistoryStore history,
        SystemInfoService systemInfo,
        SettingsStore settings,
        ILogger<RegistryViewModel> logger)
    {
        _scanner = scanner;
        _cleaner = cleaner;
        _history = history;
        _settings = settings;
        _logger = logger;

        // Dil değişince tüm metinler yeniden okunmalı; boş ad her bağlamayı tazeliyor.
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);

        IsElevated = systemInfo.Capture().IsElevated;

        Scanners = new ObservableCollection<RegistryScannerNodeViewModel>(
            scanner.Scanners.Select(s => new RegistryScannerNodeViewModel(s, IsElevated, UpdateSelection)));

        UpdateSelection();
    }

    public ObservableCollection<RegistryScannerNodeViewModel> Scanners { get; }

    public bool IsBusy => Stage is CleanerStage.Scanning or CleanerStage.Cleaning;

    public string ProgressPercentLabel => $"%{Math.Round(ProgressFraction * 100)}";

    public string FoundLabel => FoundCount == 0 ? "—" : $"{FoundCount:N0}";

    public string SelectedLabel => SelectedCount == 0 ? "—" : $"{SelectedCount:N0}";

    // ------------------------------------------------------------------ tarama

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        Stage = CleanerStage.Scanning;
        StatusMessage = string.Empty;
        ResultSummary = string.Empty;
        ResultDetail = string.Empty;
        CanUndoLastClean = false;
        ProgressFraction = 0;
        FoundCount = 0;
        BusyTitle = T("Rg_Busy_Scan");
        BusyDetail = T("Rg_Busy_ScanDetail");

        foreach (RegistryScannerNodeViewModel node in Scanners)
        {
            node.Reset();
        }

        _cancellation = new CancellationTokenSource();

        var options = new RegistryScanOptions
        {
            IsElevated = IsElevated,
            EnabledScannerIds = Scanners.Where(s => s.IsSelected).Select(s => s.Scanner.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

        var progress = new Progress<RegistryScanProgress>(report =>
        {
            ProgressFraction = report.Fraction;
            ProgressLabel = report.CurrentScanner;
            FoundCount = report.FindingsSoFar;
            BusyDetail = T("Rg_ScanProgress", report.Completed, report.Total, $"{report.FindingsSoFar:N0}");
            OnPropertyChanged(nameof(FoundLabel));
        });

        try
        {
            _lastReport = await _scanner.ScanAsync(options, progress, _cancellation.Token);
            ApplyReport(_lastReport);

            Stage = CleanerStage.Reviewing;
            StatusMessage = _lastReport.TotalCount == 0
                ? T("Rg_NothingFound")
                : T("Rg_Found",
                    $"{_lastReport.TotalCount:N0}",
                    DurationText.FromMilliseconds((int)_lastReport.Duration.TotalMilliseconds));
        }
        catch (OperationCanceledException)
        {
            Stage = CleanerStage.Ready;
            StatusMessage = T("Msg_Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registry taraması başarısız");
            Stage = CleanerStage.Ready;
            StatusMessage = T("Err_ScanError", ex.Message);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            RefreshCommands();
        }
    }

    private bool CanScan() => !IsBusy;

    // ------------------------------------------------------------------ temizlik

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        RegistryFinding[] selection = SelectedFindings();

        if (selection.Length == 0)
        {
            return;
        }

        Stage = CleanerStage.Cleaning;
        ProgressFraction = 0;
        BusyTitle = "Registry temizleniyor";
        BusyDetail = T("Rg_Busy_Backup");
        ProgressLabel = string.Empty;

        _cancellation = new CancellationTokenSource();

        var progress = new Progress<RegistryCleanProgress>(report =>
        {
            ProgressFraction = report.Fraction;
            BusyDetail = T("Rg_CleanProgress", $"{report.Processed:N0}", $"{report.Total:N0}");
        });

        try
        {
            _lastClean = await _cleaner.CleanAsync(
                selection,
                // Geri yükleme noktası tercihi Ayarlar'dan geliyor; varsayılan açık.
                new RegistryCleanOptions { CreateRestorePoint = _settings.Current.CreateRestorePoint },
                progress,
                _cancellation.Token);

            Stage = CleanerStage.Finished;
            BuildResultText(_lastClean);
            CanUndoLastClean = _lastClean.IsReversible;

            foreach (RegistryScannerNodeViewModel node in Scanners.Where(n => n.IsSelected))
            {
                node.Reset();
            }

            FoundCount = 0;
            OnPropertyChanged(nameof(FoundLabel));
            UpdateSelection();
        }
        catch (OperationCanceledException)
        {
            Stage = CleanerStage.Reviewing;
            StatusMessage = T("Rg_CleanCancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registry temizliği başarısız");
            Stage = CleanerStage.Reviewing;
            StatusMessage = T("Err_CleanError", ex.Message);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            RefreshCommands();
        }
    }

    private bool CanClean() => Stage == CleanerStage.Reviewing && SelectedFindings().Length > 0;

    [RelayCommand]
    private void Cancel()
    {
        _cancellation?.Cancel();
        BusyDetail = "iptal ediliyor...";
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_lastClean?.BackupPath is not { } backup)
        {
            return;
        }

        if (RegistryCleanEngine.Restore(backup))
        {
            _history.MarkReverted(_lastClean.RunId);
            CanUndoLastClean = false;
            ResultSummary = T("Rg_Restored");
            ResultDetail = T("Rg_BackupFile", backup);
        }
        else
        {
            ResultDetail = T("Rg_RestoreFailed", backup);
        }

        RefreshCommands();
    }

    private bool CanUndo() => CanUndoLastClean;

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (RegistryScannerNodeViewModel node in Scanners)
        {
            node.IsSelected = node.Scanner.DefaultEnabled;
        }
    }

    [RelayCommand]
    private static void ToggleDetails(RegistryScannerNodeViewModel node) => node.IsExpanded = !node.IsExpanded;

    // ------------------------------------------------------------------ iç işler

    private void ApplyReport(RegistryScanReport report)
    {
        var byId = report.Results.ToDictionary(r => r.Scanner.Id, StringComparer.OrdinalIgnoreCase);

        foreach (RegistryScannerNodeViewModel node in Scanners)
        {
            if (byId.TryGetValue(node.Scanner.Id, out RegistryScannerResult? result))
            {
                node.Apply(result);
            }
        }

        FoundCount = report.TotalCount;
        OnPropertyChanged(nameof(FoundLabel));
        UpdateSelection();
    }

    private RegistryFinding[] SelectedFindings()
    {
        HashSet<string> ids = Scanners.Where(s => s.IsSelected)
            .Select(s => s.Scanner.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _lastReport.Results
            .Where(r => ids.Contains(r.Scanner.Id))
            .SelectMany(r => r.Findings)
            .ToArray();
    }

    private void UpdateSelection()
    {
        SelectedCount = SelectedFindings().Length;
        OnPropertyChanged(nameof(SelectedLabel));
        RefreshCommands();
    }

    private void BuildResultText(RegistryCleanResult result)
    {
        ResultSummary = T("Rg_Removed", $"{result.Removed:N0}");

        var parts = new List<string>();

        if (result.BackupPath is not null)
        {
            parts.Add(T("Rg_BackupTaken"));
        }

        if (result.RestorePoint is { } restorePoint)
        {
            parts.Add(restorePoint.Describe().TrimEnd('.').ToLowerInvariant());
        }

        if (result.SkippedByGuard > 0)
        {
            parts.Add(T("Rg_GuardSkipped", $"{result.SkippedByGuard:N0}"));
        }

        if (result.Failures.Count > 0)
        {
            parts.Add(T("Rg_Failures", $"{result.Failures.Count:N0}"));
        }

        ResultDetail = string.Join(" · ", parts);
    }

    private void SetAll(bool selected)
    {
        foreach (RegistryScannerNodeViewModel node in Scanners)
        {
            node.IsSelected = selected;
        }
    }

    private void RefreshCommands()
    {
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsBusy));
    }

    partial void OnStageChanged(CleanerStage value) => RefreshCommands();

    partial void OnProgressFractionChanged(double value) => OnPropertyChanged(nameof(ProgressPercentLabel));
}
