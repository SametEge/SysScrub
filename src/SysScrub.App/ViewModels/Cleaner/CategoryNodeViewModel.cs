using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SysScrub.Core.Formatting;
using SysScrub.Core.Rules;

namespace SysScrub.App.ViewModels.Cleaner;

/// <summary>Ağaçtaki bir grup (örneğin "Google Chrome").</summary>
public sealed partial class GroupNodeViewModel : ObservableObject
{
    public GroupNodeViewModel(string name, IReadOnlyList<RuleNodeViewModel> rules)
    {
        Name = name;
        Rules = new ObservableCollection<RuleNodeViewModel>(rules);
    }

    public string Name { get; }

    public ObservableCollection<RuleNodeViewModel> Rules { get; }

    public long Bytes => Rules.Sum(r => r.Bytes);

    public bool HasFindings => Rules.Any(r => r.FileCount > 0);

    public void RaiseTotals()
    {
        OnPropertyChanged(nameof(Bytes));
        OnPropertyChanged(nameof(HasFindings));
    }
}

/// <summary>Ağacın en üst seviyesi: Windows, Tarayıcılar, Uygulamalar…</summary>
public sealed partial class CategoryNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = true;

    public CategoryNodeViewModel(RuleCategory category, IReadOnlyList<GroupNodeViewModel> groups)
    {
        Category = category;
        Groups = new ObservableCollection<GroupNodeViewModel>(groups);
    }

    public RuleCategory Category { get; }

    public ObservableCollection<GroupNodeViewModel> Groups { get; }

    public IEnumerable<RuleNodeViewModel> AllRules => Groups.SelectMany(g => g.Rules);

    public string Title => Category switch
    {
        RuleCategory.Windows => "Windows",
        RuleCategory.Browsers => "Tarayıcılar",
        RuleCategory.Applications => "Uygulamalar",
        RuleCategory.Gaming => "Oyun platformları",
        RuleCategory.Developer => "Geliştirici araçları",
        RuleCategory.Privacy => "Gizlilik",
        _ => "Diğer"
    };

    public string IconKey => Category switch
    {
        RuleCategory.Windows => "IconDashboard",
        RuleCategory.Browsers => "IconSearch",
        RuleCategory.Applications => "IconPrograms",
        RuleCategory.Gaming => "IconStartup",
        RuleCategory.Developer => "IconRegistry",
        RuleCategory.Privacy => "IconStatusInfo",
        _ => "IconCleaner"
    };

    public long Bytes => AllRules.Sum(r => r.Bytes);

    public string SizeLabel => Bytes > 0 ? ByteSize.Format(Bytes) : string.Empty;

    public int SelectedCount => AllRules.Count(r => r.IsSelected);

    public string SelectionLabel => $"{SelectedCount}/{AllRules.Count()}";

    /// <summary>
    /// Üç durumlu seçim: hepsi seçili (true), hiçbiri (false), karışık (null).
    /// Kullanıcı tıklayınca hepsi seçilir veya hepsi bırakılır.
    /// </summary>
    public bool? IsSelected
    {
        get
        {
            int selected = SelectedCount;

            if (selected == 0)
            {
                return false;
            }

            return selected == AllRules.Count() ? true : null;
        }
        set
        {
            bool target = value ?? false;

            foreach (RuleNodeViewModel rule in AllRules)
            {
                rule.IsSelected = target;
            }

            OnPropertyChanged();
        }
    }

    public void RaiseTotals()
    {
        foreach (GroupNodeViewModel group in Groups)
        {
            group.RaiseTotals();
        }

        OnPropertyChanged(nameof(Bytes));
        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionLabel));
    }
}
