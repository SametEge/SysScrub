using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SysScrub.App.Localization;
using static SysScrub.App.Localization.L;
using CommunityToolkit.Mvvm.Input;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Formatting;

namespace SysScrub.App.ViewModels;

/// <summary>Zaman tünelindeki tek bir satır.</summary>
public sealed partial class TimelineEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isReversible;

    [ObservableProperty]
    private bool _wasReverted;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private IReadOnlyList<TimelineItemViewModel> _items = [];

    public TimelineEntryViewModel(HistoryRun run)
    {
        Run = run;
        _isReversible = run.IsReversible;
        _wasReverted = run.WasReverted;
    }

    public HistoryRun Run { get; }

    public Guid RunId => Run.RunId;

    public string Title => Run.Operation switch
    {
        HistoryOperation.Clean => T("Tl_Op_Clean"),
        HistoryOperation.RegistryClean => T("Tl_Op_Registry"),
        HistoryOperation.DriverUpdate => T("Tl_Op_Driver"),
        HistoryOperation.StartupChange => T("Tl_Op_Startup"),
        HistoryOperation.Uninstall => T("Tl_Op_Uninstall"),
        _ => T("Tl_Op_Other")
    };

    public string TimeLabel => Run.StartedAt.LocalDateTime.ToString("dd MMMM yyyy, HH:mm");

    public string GainLabel => ByteSize.Format(Run.BytesFreed);

    public string DetailLabel
    {
        get
        {
            var parts = new List<string> { T("Tl_Items", $"{Run.ItemsAffected:N0}") };

            if (Run.ItemsFailed > 0)
            {
                parts.Add(T("Tl_Failed", $"{Run.ItemsFailed:N0}"));
            }

            if (Run.ItemsScheduledForReboot > 0)
            {
                parts.Add(T("Tl_Reboot", $"{Run.ItemsScheduledForReboot:N0}"));
            }

            // Kanıtlı ölçüm: diskin gerçekten ne kadar boşaldığı.
            if (Run.MeasuredGain > 0)
            {
                parts.Add(T("Tl_Measured", ByteSize.Format(Run.MeasuredGain)));
            }

            parts.Add(DurationText.FromMilliseconds((int)Run.Duration.TotalMilliseconds));

            return string.Join(" · ", parts);
        }
    }

    public string StateLabel => T(WasReverted ? "Tl_St_Reverted"
        : IsReversible ? "Tl_St_Reversible"
        : "Tl_St_Permanent");

    public bool CanUndo => IsReversible && !WasReverted;
}

/// <summary>Bir çalıştırmanın ayrıntısındaki tek dosya.</summary>
public sealed record TimelineItemViewModel(string Path, string SizeLabel, string OutcomeLabel, bool IsProblem);

/// <summary>
/// Zaman tüneli: sistemde yapılan her değişikliğin kronolojik kaydı.
///
/// Sonraki fazlarda registry, sürücü ve başlangıç işlemleri de aynı akışa yazılacak;
/// kullanıcı "ne zaman ne değişti" sorusunu tek yerden cevaplayabilecek.
/// </summary>
public sealed partial class TimelineViewModel : ObservableObject
{
    private readonly HistoryStore _history;
    private readonly QuarantineStore _quarantine;

    [ObservableProperty]
    private string _emptyMessage = string.Empty;

    [ObservableProperty]
    private string _quarantineLabel = string.Empty;

    public TimelineViewModel(HistoryStore history, QuarantineStore quarantine)
    {
        _history = history;
        _quarantine = quarantine;
        Entries = [];

        Refresh();
    
        // Dil değişince tüm metinler yeniden okunmalı; boş ad her bağlamayı tazeliyor.
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);
}

    public ObservableCollection<TimelineEntryViewModel> Entries { get; }

    public bool IsEmpty => Entries.Count == 0;

    [RelayCommand]
    public void Refresh()
    {
        Entries.Clear();

        foreach (HistoryRun run in _history.ListRuns())
        {
            Entries.Add(new TimelineEntryViewModel(run));
        }

        EmptyMessage = T("Tl_EmptyMessage");

        long quarantineBytes = _quarantine.TotalBytes();
        QuarantineLabel = quarantineBytes > 0
            ? T("Tl_QuarantineWaiting", ByteSize.Format(quarantineBytes))
            : string.Empty;

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void ToggleDetails(TimelineEntryViewModel entry)
    {
        if (entry.IsExpanded)
        {
            entry.IsExpanded = false;
            return;
        }

        if (entry.Items.Count == 0)
        {
            entry.Items = _history.ListItems(entry.RunId)
                .OrderByDescending(i => i.Bytes)
                .Take(200)
                .Select(item => new TimelineItemViewModel(
                    item.Path,
                    item.Bytes > 0 ? ByteSize.Format(item.Bytes) : string.Empty,
                    DescribeOutcome(item.Outcome),
                    item.Outcome is HistoryItemOutcome.Failed or HistoryItemOutcome.SkippedByGuard))
                .ToArray();
        }

        entry.IsExpanded = true;
    }

    [RelayCommand]
    private void Undo(TimelineEntryViewModel entry)
    {
        RestoreResult result = _quarantine.Restore(entry.RunId);
        _history.MarkReverted(entry.RunId);

        entry.WasReverted = true;
        entry.IsReversible = false;

        QuarantineLabel = result.Restored > 0
            ? T("Tl_RestoredFiles", $"{result.Restored:N0}", ByteSize.Format(result.Bytes))
            : T("Tl_NothingToRestore");

        Refresh();
    }

    private static string DescribeOutcome(HistoryItemOutcome outcome) => outcome switch
    {
        HistoryItemOutcome.Deleted => T("Tl_Out_Deleted"),
        HistoryItemOutcome.Quarantined => T("Tl_Out_Quarantined"),
        HistoryItemOutcome.RecycleBin => T("Tl_Out_RecycleBin"),
        HistoryItemOutcome.ScheduledForReboot => T("Tl_Out_Reboot"),
        HistoryItemOutcome.SkippedByGuard => T("Tl_Out_Guard"),
        HistoryItemOutcome.Changed => T("Tl_Out_Changed"),
        _ => T("Tl_Out_Failed")
    };
}
