using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysScrub.App.Localization;
using SysScrub.App.Services;

namespace SysScrub.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ThemeService _theme;

    [ObservableProperty]
    private NavigationItem? _selectedItem;

    [ObservableProperty]
    private NavigationItem? _selectedFooterItem;

    [ObservableProperty]
    private string _themeLabel = LocalizationService.Instance["Theme_System"];

    public MainWindowViewModel(ThemeService theme)
    {
        _theme = theme;

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            ThemeLabel = ThemeLabelFor(_theme.Mode);
            OnPropertyChanged(nameof(VersionLabel));
        };

        Items =
        [
            new NavigationItem
            {
                Id = "Panel",
                TitleKey = "Nav_Dashboard",
                IconKey = "IconDashboard",
                DescriptionKey = "Mod_Dashboard",
                Phase = 0,
                TemplateKey = "DashboardPageTemplate"
            },
            new NavigationItem
            {
                Id = "Temizleyici",
                TitleKey = "Nav_Cleaner",
                IconKey = "IconCleaner",
                DescriptionKey = "Mod_Cleaner",
                Phase = 0,
                TemplateKey = "CleanerPageTemplate"
            },
            new NavigationItem
            {
                Id = "Registry",
                TitleKey = "Nav_Registry",
                IconKey = "IconRegistry",
                DescriptionKey = "Mod_Registry",
                Phase = 0,
                TemplateKey = "RegistryPageTemplate"
            },
            new NavigationItem
            {
                Id = "Sürücüler",
                TitleKey = "Nav_Drivers",
                IconKey = "IconDrivers",
                DescriptionKey = "Mod_Drivers",
                Phase = 0,
                TemplateKey = "DriversPageTemplate"
            },
            new NavigationItem
            {
                Id = "Güncellemeler",
                TitleKey = "Nav_Updates",
                IconKey = "IconUpdates",
                DescriptionKey = "Mod_Updates",
                Phase = 0,
                TemplateKey = "SoftwareUpdatesPageTemplate"
            },
            new NavigationItem
            {
                Id = "Başlangıç",
                TitleKey = "Nav_Startup",
                IconKey = "IconStartup",
                DescriptionKey = "Mod_Startup",
                Phase = 0,
                TemplateKey = "StartupPageTemplate"
            },
            new NavigationItem
            {
                Id = "Programlar",
                TitleKey = "Nav_Programs",
                IconKey = "IconPrograms",
                DescriptionKey = "Mod_Programs",
                Phase = 0,
                TemplateKey = "ProgramsPageTemplate"
            },
            new NavigationItem
            {
                Id = "Disk sağlığı",
                TitleKey = "Nav_DiskHealth",
                IconKey = "IconDiskHealth",
                DescriptionKey = "Mod_DiskHealth",
                Phase = 0,
                TemplateKey = "DiskHealthPageTemplate"
            },
            new NavigationItem
            {
                Id = "Disk analizi",
                TitleKey = "Nav_DiskAnalysis",
                IconKey = "IconDiskAnalysis",
                DescriptionKey = "Mod_DiskAnalysis",
                Phase = 0,
                TemplateKey = "DiskAnalysisPageTemplate"
            },
            new NavigationItem
            {
                Id = "Zaman tüneli",
                TitleKey = "Nav_Timeline",
                IconKey = "IconTimeline",
                DescriptionKey = "Mod_Timeline",
                Phase = 0,
                TemplateKey = "TimelinePageTemplate"
            }
        ];

        FooterItems =
        [
            new NavigationItem
            {
                Id = "Ayarlar",
                TitleKey = "Nav_Settings",
                IconKey = "IconSettings",
                DescriptionKey = "Mod_Settings",
                Phase = 0,
                TemplateKey = "SettingsPageTemplate",
                IsFooterItem = true
            }
        ];

        SelectedItem = Items[0];
    }

    public ObservableCollection<NavigationItem> Items { get; }

    public ObservableCollection<NavigationItem> FooterItems { get; }

    public string VersionLabel => typeof(MainWindowViewModel).Assembly.GetName().Version is { } v
        ? LocalizationService.Instance.Format("App_Version", $"{v.Major}.{v.Minor}.{v.Build}")
        : string.Empty;

    private static string ThemeLabelFor(AppThemeMode mode) => mode switch
    {
        AppThemeMode.Light => LocalizationService.Instance["Theme_Light"],
        AppThemeMode.Dark => LocalizationService.Instance["Theme_Dark"],
        _ => LocalizationService.Instance["Theme_System"]
    };

    /// <summary>Sağda gösterilen modül: iki listeden hangisi seçiliyse o.</summary>
    public NavigationItem? CurrentItem => SelectedItem ?? SelectedFooterItem;

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        if (value is not null)
        {
            SelectedFooterItem = null;
        }

        OnPropertyChanged(nameof(CurrentItem));
    }

    partial void OnSelectedFooterItemChanged(NavigationItem? value)
    {
        if (value is not null)
        {
            SelectedItem = null;
        }

        OnPropertyChanged(nameof(CurrentItem));
    }

    [RelayCommand]
    private void CycleTheme()
    {
        _theme.Mode = _theme.Mode switch
        {
            AppThemeMode.System => AppThemeMode.Light,
            AppThemeMode.Light => AppThemeMode.Dark,
            _ => AppThemeMode.System
        };

        ThemeLabel = ThemeLabelFor(_theme.Mode);
    }
}
