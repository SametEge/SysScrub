using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
        HistoryOperation.Clean => "Temizlik",
        HistoryOperation.RegistryClean => "Registry temizliği",
        HistoryOperation.DriverUpdate => "Sürücü güncellemesi",
        HistoryOperation.StartupChange => "Başlangıç değişikliği",
        HistoryOperation.Uninstall => "Program kaldırma",
        _ => "İşlem"
    };

    public string TimeLabel => Run.StartedAt.LocalDateTime.ToString("dd MMMM yyyy, HH:mm");

    public string GainLabel => ByteSize.Format(Run.BytesFreed);

    public string DetailLabel
    {
        get
        {
            var parts = new List<string> { $"{Run.ItemsAffected:N0} öğe" };

            if (Run.ItemsFailed > 0)
            {
                parts.Add($"{Run.ItemsFailed:N0} başarısız");
            }

            if (Run.ItemsScheduledForReboot > 0)
            {
                parts.Add($"{Run.ItemsScheduledForReboot:N0} yeniden başlatmada silinecek");
            }

            // Kanıtlı ölçüm: diskin gerçekten ne kadar boşaldığı.
            if (Run.MeasuredGain > 0)
            {
                parts.Add($"diskte ölçülen {ByteSize.Format(Run.MeasuredGain)}");
            }

            parts.Add($"{Run.Duration.TotalSeconds:F1} sn");

            return string.Join(" · ", parts);
        }
    }

    public string StateLabel => WasReverted ? "geri alındı"
        : IsReversible ? "geri alınabilir"
        : "kalıcı";

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

        EmptyMessage = "Henüz kayıtlı bir işlem yok. Temizleyiciyi çalıştırdığında yaptığın her değişiklik buraya düşecek.";

        long quarantineBytes = _quarantine.TotalBytes();
        QuarantineLabel = quarantineBytes > 0
            ? $"Karantinada {ByteSize.Format(quarantineBytes)} veri bekliyor — saklama süresi dolunca kalıcı silinir."
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
            ? $"{result.Restored:N0} dosya geri yüklendi ({ByteSize.Format(result.Bytes)})."
            : "Geri yüklenecek dosya bulunamadı.";

        Refresh();
    }

    private static string DescribeOutcome(HistoryItemOutcome outcome) => outcome switch
    {
        HistoryItemOutcome.Deleted => "silindi",
        HistoryItemOutcome.Quarantined => "karantinada",
        HistoryItemOutcome.RecycleBin => "geri dönüşüm kutusunda",
        HistoryItemOutcome.ScheduledForReboot => "yeniden başlatmada silinecek",
        HistoryItemOutcome.SkippedByGuard => "güvenlik denetimi atladı",
        HistoryItemOutcome.Changed => "değiştirildi",
        _ => "silinemedi"
    };
}
