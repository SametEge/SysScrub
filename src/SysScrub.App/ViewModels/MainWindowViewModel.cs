using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private string _themeLabel = "Sistem teması";

    public MainWindowViewModel(ThemeService theme)
    {
        _theme = theme;

        Items =
        [
            new NavigationItem
            {
                Title = "Panel",
                TitleKey = "Nav_Dashboard",
                IconKey = "IconDashboard",
                Description = "Sistemin tek bakışta özeti: temizlenebilir alan, disk sağlığı, bekleyen güncellemeler ve akıllı öneriler.",
                Phase = 0,
                TemplateKey = "DashboardPageTemplate"
            },
            new NavigationItem
            {
                Title = "Temizleyici",
                TitleKey = "Nav_Cleaner",
                IconKey = "IconCleaner",
                Description = "Windows, tarayıcı ve uygulama artıklarını kural tabanlı tarar. Her silme karantinaya alınır ve geri alınabilir.",
                Phase = 0,
                TemplateKey = "CleanerPageTemplate"
            },
            new NavigationItem
            {
                Title = "Registry",
                TitleKey = "Nav_Registry",
                IconKey = "IconRegistry",
                Description = "Ölü kayıt defteri girdilerini 12 ayrı tarayıcıyla bulur. Her işlem öncesi .reg yedeği ve geri yükleme noktası alınır.",
                Phase = 0,
                TemplateKey = "RegistryPageTemplate"
            },
            new NavigationItem
            {
                Title = "Sürücüler",
                TitleKey = "Nav_Drivers",
                IconKey = "IconDrivers",
                Description = "Donanım envanteri, eski sürücü tespiti ve güncelleme. Kaynaklar Microsoft ve üreticinin resmi kanalları.",
                Phase = 0,
                TemplateKey = "DriversPageTemplate"
            },
            new NavigationItem
            {
                Title = "Güncellemeler",
                TitleKey = "Nav_Updates",
                IconKey = "IconUpdates",
                Description = "Kurulu programların yeni sürümlerini winget üzerinden bulur ve toplu günceller.",
                Phase = 0,
                TemplateKey = "SoftwareUpdatesPageTemplate"
            },
            new NavigationItem
            {
                Title = "Başlangıç",
                TitleKey = "Nav_Startup",
                IconKey = "IconStartup",
                Description = "Açılışta çalışan her şey tek listede. Etkisi tahmin değil, olay günlüğünden okunan gerçek gecikme.",
                Phase = 0,
                TemplateKey = "StartupPageTemplate"
            },
            new NavigationItem
            {
                Title = "Programlar",
                TitleKey = "Nav_Programs",
                IconKey = "IconPrograms",
                Description = "Kurulu programları toplu kaldırır, kaldırma sonrası artık dosya ve kayıtları tarar.",
                Phase = 0,
                TemplateKey = "ProgramsPageTemplate"
            },
            new NavigationItem
            {
                Title = "Disk sağlığı",
                TitleKey = "Nav_DiskHealth",
                IconKey = "IconDiskHealth",
                Description = "S.M.A.R.T. verisi, sıcaklık geçmişi, yazılan toplam veri ve kalan ömür tahmini — sade açıklamalarla.",
                Phase = 0,
                TemplateKey = "DiskHealthPageTemplate"
            },
            new NavigationItem
            {
                Title = "Disk analizi",
                TitleKey = "Nav_DiskAnalysis",
                IconKey = "IconDiskAnalysis",
                Description = "Alanı ne yiyor? Treemap görselleştirme, en büyük dosyalar ve yinelenen dosya bulucu.",
                Phase = 0,
                TemplateKey = "DiskAnalysisPageTemplate"
            },
            new NavigationItem
            {
                Title = "Zaman tüneli",
                TitleKey = "Nav_Timeline",
                IconKey = "IconTimeline",
                Description = "Sistemde yapılan her değişikliğin kronolojik kaydı. Herhangi bir noktaya tek tıkla dönülür.",
                Phase = 0,
                TemplateKey = "TimelinePageTemplate"
            }
        ];

        FooterItems =
        [
            new NavigationItem
            {
                Title = "Ayarlar",
                TitleKey = "Nav_Settings",
                IconKey = "IconSettings",
                Description = "Tema, dil, zamanlanmış görevler, karantina saklama süresi ve gelişmiş seçenekler.",
                Phase = 0,
                TemplateKey = "SettingsPageTemplate",
                IsFooterItem = true
            }
        ];

        SelectedItem = Items[0];
    }

    public ObservableCollection<NavigationItem> Items { get; }

    public ObservableCollection<NavigationItem> FooterItems { get; }

    public string VersionLabel { get; } =
        typeof(MainWindowViewModel).Assembly.GetName().Version is { } v
            ? $"sürüm {v.Major}.{v.Minor}.{v.Build}"
            : "sürüm bilinmiyor";

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

        ThemeLabel = _theme.Mode switch
        {
            AppThemeMode.Light => "Açık tema",
            AppThemeMode.Dark => "Koyu tema",
            _ => "Sistem teması"
        };
    }
}
