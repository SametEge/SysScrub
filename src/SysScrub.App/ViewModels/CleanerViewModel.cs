using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysScrub.App.Localization;
using static SysScrub.App.Localization.L;
using SysScrub.App.ViewModels.Cleaner;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;
using SysScrub.Core.Rules;
using SysScrub.Core.Settings;

namespace SysScrub.App.ViewModels;

public enum CleanerStage
{
    /// <summary>Henüz taranmadı; kural listesi seçilebilir durumda.</summary>
    Ready,
    Scanning,
    Reviewing,
    Cleaning,
    Finished
}

/// <summary>
/// Temizleyici ekranı: tara → incele → temizle.
///
/// Üç aşamanın ayrı olması bilinçli. Tarama zararsızdır ve istendiği kadar
/// tekrarlanabilir; silme ancak kullanıcı ne olacağını gördükten sonra başlar.
/// </summary>
public sealed partial class CleanerViewModel : ObservableObject
{
    private readonly RuleSet _ruleSet;
    private readonly ScanEngine _scanner;
    private readonly CleanEngine _cleaner;
    private readonly QuarantineStore _quarantine;
    private readonly HistoryStore _history;
    private readonly SystemInfoService _systemInfo;
    private readonly SettingsStore _settings;
    private readonly ILogger<CleanerViewModel> _logger;

    private CancellationTokenSource? _cancellation;
    private ScanReport _lastReport = ScanReport.Empty;
    private CleanResult? _lastClean;

    [ObservableProperty]
    private CleanerStage _stage = CleanerStage.Ready;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string _progressLabel = string.Empty;

    /// <summary>Örtü ekranındaki başlık: "Taranıyor" / "Temizleniyor".</summary>
    [ObservableProperty]
    private string _busyTitle = string.Empty;

    /// <summary>Örtü ekranındaki sayaç satırı.</summary>
    [ObservableProperty]
    private string _busyDetail = string.Empty;

    [ObservableProperty]
    private long _foundBytes;

    [ObservableProperty]
    private int _foundFiles;

    [ObservableProperty]
    private long _selectedBytes;

    [ObservableProperty]
    private int _selectedRuleCount;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    [ObservableProperty]
    private string _resultDetail = string.Empty;

    [ObservableProperty]
    private bool _canUndoLastClean;

    public CleanerViewModel(
        RuleSet ruleSet,
        ScanEngine scanner,
        CleanEngine cleaner,
        QuarantineStore quarantine,
        HistoryStore history,
        SystemInfoService systemInfo,
        SettingsStore settings,
        ILogger<CleanerViewModel> logger)
    {
        _settings = settings;
        _ruleSet = ruleSet;
        _scanner = scanner;
        _cleaner = cleaner;
        _quarantine = quarantine;
        _history = history;
        _systemInfo = systemInfo;
        _logger = logger;

        // Dil değişince tüm metinler yeniden okunmalı; boş ad her bağlamayı tazeliyor.
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);

        IsElevated = systemInfo.Capture().IsElevated;
        Categories = new ObservableCollection<CategoryNodeViewModel>(BuildTree());

        UpdateSelectionTotals();
    }

    public ObservableCollection<CategoryNodeViewModel> Categories { get; }

    public IEnumerable<RuleNodeViewModel> AllRules => Categories.SelectMany(c => c.AllRules);

    public bool IsBusy => Stage is CleanerStage.Scanning or CleanerStage.Cleaning;

    public bool CanEditSelection => Stage is CleanerStage.Ready or CleanerStage.Reviewing or CleanerStage.Finished;

    public string ProgressPercentLabel => $"%{Math.Round(ProgressFraction * 100)}";

    public string FoundLabel => FoundFiles == 0 ? "—" : ByteSize.Format(FoundBytes);

    public string SelectedLabel => SelectedBytes == 0 ? "—" : ByteSize.Format(SelectedBytes);

    // ------------------------------------------------------------------ tarama

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task ScanAsync()
    {
        Stage = CleanerStage.Scanning;
        StatusMessage = string.Empty;
        ResultSummary = string.Empty;
        ResultDetail = string.Empty;
        CanUndoLastClean = false;
        ProgressFraction = 0;
        FoundBytes = 0;
        FoundFiles = 0;
        BusyTitle = T("Cl_Busy_Scan");
        BusyDetail = T("Cl_Busy_ScanDetail");
        ProgressLabel = string.Empty;

        foreach (RuleNodeViewModel rule in AllRules)
        {
            rule.ResetScan();
        }

        _cancellation = new CancellationTokenSource();

        var options = new ScanOptions
        {
            IsElevated = IsElevated,
            EnabledRuleIds = AllRules.Where(r => r.IsSelected).Select(r => r.Rule.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

        var progress = new Progress<ScanProgress>(report =>
        {
            ProgressFraction = report.Fraction;
            ProgressLabel = report.CurrentRule;
            FoundBytes = report.BytesFound;
            FoundFiles = report.FilesFound;

            BusyDetail = report.FilesFound == 0
                ? T("Cl_RulesScanned", report.CompletedRules, report.TotalRules)
                : T("Cl_FoundFiles", $"{report.FilesFound:N0}", ByteSize.Format(report.BytesFound));

            OnPropertyChanged(nameof(FoundLabel));
        });

        try
        {
            _lastReport = await _scanner.ScanAsync(_ruleSet, options, progress, _cancellation.Token);
            ApplyReport(_lastReport);

            Stage = CleanerStage.Reviewing;
            StatusMessage = _lastReport.TotalCount == 0
                ? T("Cl_NothingFound")
                : BuildScanSummary(_lastReport);
        }
        catch (OperationCanceledException)
        {
            Stage = CleanerStage.Ready;
            StatusMessage = T("Msg_Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tarama başarısız");
            Stage = CleanerStage.Ready;
            StatusMessage = T("Err_ScanError", ex.Message);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            RefreshCommandStates();
        }
    }

    private bool CanStartScan() => !IsBusy;

    // ------------------------------------------------------------------ temizlik

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        IReadOnlyList<RuleScanResult> selection = SelectedResults();

        if (selection.Count == 0)
        {
            return;
        }

        Stage = CleanerStage.Cleaning;
        ProgressFraction = 0;
        StatusMessage = string.Empty;
        BusyTitle = T("Cl_Busy_Clean");
        BusyDetail = T("Cl_FilesOf", "0", $"{selection.Sum(s => s.Count):N0}");
        ProgressLabel = string.Empty;

        _cancellation = new CancellationTokenSource();

        var progress = new Progress<CleanProgress>(report =>
        {
            ProgressFraction = report.Fraction;
            ProgressLabel = report.CurrentRule;
            BusyDetail = T("Cl_FilesOf", $"{report.Processed:N0}", $"{report.Total:N0}") + "  ·  " +
                         T("Cl_Recovered", ByteSize.Format(report.BytesFreed));
        });

        try
        {
            _lastClean = await _cleaner.CleanAsync(
                selection,
                // Karantina saklama süresi Ayarlar'dan geliyor.
                new CleanOptions { QuarantineRetention = _settings.Current.QuarantineRetention },
                progress,
                _cancellation.Token);

            Stage = CleanerStage.Finished;
            BuildResultText(_lastClean);
            CanUndoLastClean = _lastClean.IsReversible;

            // Temizlenen kurallar artık boş; listedeki sayılar gerçeği yansıtsın.
            foreach (RuleNodeViewModel rule in AllRules.Where(r => selection.Any(s => s.Rule.Id == r.Rule.Id)))
            {
                rule.ResetScan();
            }

            RaiseTreeTotals();
        }
        catch (OperationCanceledException)
        {
            Stage = CleanerStage.Reviewing;
            StatusMessage = T("Cl_CleanCancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Temizlik başarısız");
            Stage = CleanerStage.Reviewing;
            StatusMessage = T("Err_CleanError", ex.Message);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            RefreshCommandStates();
        }
    }

    private bool CanClean() => Stage == CleanerStage.Reviewing && SelectedBytes > 0;

    [RelayCommand]
    private void Cancel()
    {
        _cancellation?.Cancel();
        BusyDetail = "iptal ediliyor...";
        StatusMessage = T("Cl_Cancelling");
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_lastClean is null)
        {
            return;
        }

        RestoreResult restore = _quarantine.Restore(_lastClean.RunId);
        _history.MarkReverted(_lastClean.RunId);

        CanUndoLastClean = false;
        ResultSummary = T("Cl_Restored", $"{restore.Restored:N0}");
        ResultDetail = restore.Skipped > 0
            ? T("Cl_RestoreSkipped", $"{restore.Skipped:N0}")
            : T("Cl_RestoreOk", ByteSize.Format(restore.Bytes));

        RefreshCommandStates();
    }

    private bool CanUndo() => CanUndoLastClean;

    [RelayCommand]
    private void SelectAll() => SetAllSelected(true);

    [RelayCommand]
    private void SelectNone() => SetAllSelected(false);

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (RuleNodeViewModel rule in AllRules)
        {
            rule.IsSelected = rule.Rule.DefaultEnabled;
        }

        RaiseTreeTotals();
    }

    // ------------------------------------------------------------------ iç işler

    private IReadOnlyList<CategoryNodeViewModel> BuildTree()
    {
        return _ruleSet.GroupForDisplay()
            .Select(category => new CategoryNodeViewModel(
                category.Category,
                category.Groups
                    .Select(group => new GroupNodeViewModel(
                        group.Name,
                        group.Rules.Select(rule => new RuleNodeViewModel(rule, IsElevated, OnRuleSelectionChanged)).ToArray()))
                    .ToArray()))
            .ToArray();
    }

    private void OnRuleSelectionChanged()
    {
        UpdateSelectionTotals();
        RaiseTreeTotals();
    }

    private void UpdateSelectionTotals()
    {
        RuleNodeViewModel[] selected = AllRules.Where(r => r.IsSelected).ToArray();

        SelectedBytes = selected.Sum(r => r.Bytes);
        SelectedRuleCount = selected.Length;

        OnPropertyChanged(nameof(SelectedLabel));
        RefreshCommandStates();
    }

    private void ApplyReport(ScanReport report)
    {
        var byRuleId = report.Results.ToDictionary(r => r.Rule.Id, StringComparer.OrdinalIgnoreCase);

        foreach (RuleNodeViewModel node in AllRules)
        {
            if (byRuleId.TryGetValue(node.Rule.Id, out RuleScanResult? result))
            {
                node.ApplyScanResult(result);
            }
        }

        FoundBytes = report.TotalBytes;
        FoundFiles = report.TotalCount;
        OnPropertyChanged(nameof(FoundLabel));

        UpdateSelectionTotals();
        RaiseTreeTotals();
    }

    private IReadOnlyList<RuleScanResult> SelectedResults()
    {
        HashSet<string> selectedIds = AllRules
            .Where(r => r.IsSelected)
            .Select(r => r.Rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _lastReport.WithFindings
            .Where(r => selectedIds.Contains(r.Rule.Id))
            .ToArray();
    }

    private string BuildScanSummary(ScanReport report)
    {
        string text = $"{ByteSize.Format(report.TotalBytes)} temizlenebilir alan bulundu " +
                      $"({report.TotalCount:N0} dosya, {report.Duration.TotalSeconds:F1} saniye).";

        if (report.SkippedForElevation > 0)
        {
            text += T("Cl_SkippedElevation", report.SkippedForElevation);
        }

        return text;
    }

    private void BuildResultText(CleanResult result)
    {
        ResultSummary = $"{ByteSize.Format(result.BytesFreed)} temizlendi";

        var parts = new List<string>();

        if (result.Quarantined > 0)
        {
            parts.Add(T("Cl_Quarantined", $"{result.Quarantined:N0}"));
        }

        if (result.Deleted > 0)
        {
            parts.Add(T("Cl_Deleted", $"{result.Deleted:N0}"));
        }

        if (result.ScheduledForReboot > 0)
        {
            parts.Add(T("Cl_Reboot", $"{result.ScheduledForReboot:N0}"));
        }

        if (result.SkippedByGuard > 0)
        {
            parts.Add(T("Cl_GuardSkipped", $"{result.SkippedByGuard:N0}"));
        }

        if (result.Failures.Count > 0)
        {
            parts.Add($"{result.Failures.Count:N0} dosya silinemedi");
        }

        // Kanıtlı ölçüm: diskin gerçek boş alanı ne kadar arttı.
        if (result.MeasuredGain > 0)
        {
            parts.Add(T("Cl_MeasuredGain", ByteSize.Format(result.MeasuredGain)));
        }

        ResultDetail = string.Join(" · ", parts);
    }

    private void RaiseTreeTotals()
    {
        foreach (CategoryNodeViewModel category in Categories)
        {
            category.RaiseTotals();
        }
    }

    private void RefreshCommandStates()
    {
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEditSelection));
    }

    private void SetAllSelected(bool selected)
    {
        foreach (RuleNodeViewModel rule in AllRules)
        {
            rule.IsSelected = selected;
        }

        RaiseTreeTotals();
    }

    partial void OnStageChanged(CleanerStage value) => RefreshCommandStates();

    partial void OnProgressFractionChanged(double value) => OnPropertyChanged(nameof(ProgressPercentLabel));
}
