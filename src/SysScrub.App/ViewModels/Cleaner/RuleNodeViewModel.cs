using CommunityToolkit.Mvvm.ComponentModel;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Formatting;
using SysScrub.Core.Rules;

namespace SysScrub.App.ViewModels.Cleaner;

/// <summary>Ağaçtaki tek bir kural satırı.</summary>
public sealed partial class RuleNodeViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private long _bytes;

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private bool _wasScanned;

    [ObservableProperty]
    private IReadOnlyList<string> _runningBlockers = [];

    private readonly bool _isElevated;

    public RuleNodeViewModel(CleaningRule rule, bool isElevated, Action onSelectionChanged)
    {
        Rule = rule;
        _isElevated = isElevated;
        _onSelectionChanged = onSelectionChanged;
        _isSelected = rule.DefaultEnabled;
    }

    public CleaningRule Rule { get; }

    /// <summary>
    /// Yönetici hakkı olmadan taranamayan kural. İşaretli ama boyutsuz bir satır
    /// kullanıcıya bozukmuş gibi görünür; sebebini satırın kendisinde söylüyoruz.
    /// </summary>
    public bool IsBlockedByElevation => Rule.RequiresAdmin && !_isElevated;

    public string Title => Rule.Name.Resolve();

    public string Explanation => Rule.Explanation?.Resolve() ?? string.Empty;

    public bool HasExplanation => Explanation.Length > 0;

    public RiskLevel Risk => Rule.Risk;

    public bool RequiresAdmin => Rule.RequiresAdmin;

    /// <summary>Taranmadan önce boş, sonra boyut. "0 B" göstermek yerine sessiz kalıyoruz.</summary>
    public string SizeLabel => IsBlockedByElevation
        ? string.Empty
        : !WasScanned ? string.Empty : Bytes > 0 ? ByteSize.Format(Bytes) : "—";

    public string ElevationNote => IsBlockedByElevation
        ? "Yönetici hakkı gerekiyor — uygulamayı yönetici olarak çalıştır."
        : string.Empty;

    public string CountLabel => !WasScanned || FileCount == 0 ? string.Empty : $"{FileCount:N0} dosya";

    /// <summary>Tarandı ve bulgu yok: satır soluklaşır ama listeden kaybolmaz.</summary>
    public bool IsEmpty => WasScanned && FileCount == 0;

    public bool HasBlockers => RunningBlockers.Count > 0;

    public string BlockersLabel => HasBlockers
        ? $"Açık: {string.Join(", ", RunningBlockers)} — bazı dosyalar kilitli olabilir"
        : string.Empty;

    public string RiskLabel => Risk switch
    {
        RiskLevel.Caution => "dikkat",
        RiskLevel.Advanced => "gelişmiş",
        _ => string.Empty
    };

    public bool ShowRiskBadge => Risk != RiskLevel.Safe;

    public void ApplyScanResult(RuleScanResult result)
    {
        Bytes = result.Bytes;
        FileCount = result.Count;
        RunningBlockers = result.RunningBlockers;
        WasScanned = true;

        RaiseDerived();
    }

    public void ResetScan()
    {
        Bytes = 0;
        FileCount = 0;
        RunningBlockers = [];
        WasScanned = false;

        RaiseDerived();
    }

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasBlockers));
        OnPropertyChanged(nameof(BlockersLabel));
    }
}
