using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SysScrub.App.Localization;
using SysScrub.Core.Formatting;
using SysScrub.Core.Rules;

namespace SysScrub.App.ViewModels.Cleaner;

/// <summary>Ağaçtaki bir grup (örneğin "Google Chrome").</summary>
public sealed partial class GroupNodeViewModel : ObservableObject
{
    public GroupNodeViewModel(string name, IReadOnlyList<RuleNodeViewModel> rules)
    {
        SourceName = name;
        Rules = new ObservableCollection<RuleNodeViewModel>(rules);

        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Name));
    }

    /// <summary>Kural dosyasındaki ham grup adı; çeviri anahtarı bundan üretiliyor.</summary>
    public string SourceName { get; }

    /// <summary>
    /// Gösterilen ad. Grup adları kural dosyasında tek dilde duruyor; katalogda
    /// karşılığı varsa çevrilmiş hâli, yoksa ham adı gösteriliyor. Böylece
    /// kullanıcının kendi eklediği kurallar da bozulmadan görünüyor.
    /// </summary>
    public string Name
    {
        get
        {
            string key = $"Grp_{SourceName}";
            string translated = LocalizationService.Instance[key];

            return translated == key ? SourceName : translated;
        }
    }

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

        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }

    public RuleCategory Category { get; }

    public ObservableCollection<GroupNodeViewModel> Groups { get; }

    public IEnumerable<RuleNodeViewModel> AllRules => Groups.SelectMany(g => g.Rules);

    public string Title => LocalizationService.Instance[Category switch
    {
        RuleCategory.Windows => "Cat_Windows",
        RuleCategory.Browsers => "Cat_Browsers",
        RuleCategory.Applications => "Cat_Applications",
        RuleCategory.Gaming => "Cat_Gaming",
        RuleCategory.Developer => "Cat_Developer",
        RuleCategory.Privacy => "Cat_Privacy",
        _ => "Cat_Other"
    }];

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
