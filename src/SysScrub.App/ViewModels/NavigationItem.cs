using CommunityToolkit.Mvvm.ComponentModel;
using SysScrub.App.Localization;

namespace SysScrub.App.ViewModels;

/// <summary>
/// Yan menüdeki bir modül. Faz numarası, o modülün henüz gelmediğini dürüstçe göstermek için var —
/// kullanıcı boş bir ekranla karşılaşıp uygulamanın bozuk olduğunu düşünmesin.
/// </summary>
public sealed partial class NavigationItem : ObservableObject
{
    public NavigationItem() =>
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Description));
        };

    /// <summary>
    /// Kararlı kimlik. Ekranda gösterilmiyor; kod içi eşleştirmeler (ekran görüntüsü
    /// anahtarı, günlük kayıtları) bunu kullanıyor ki dil değişince bozulmasınlar.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Menüde gösterilen ad; dil değişince kendini yeniliyor.</summary>
    public required string TitleKey { get; init; }

    public string Title => LocalizationService.Instance[TitleKey];

    /// <summary>Themes/Icons.xaml içindeki Geometry anahtarı.</summary>
    public required string IconKey { get; init; }

    /// <summary>Modülün ne yaptığını anlatan cümlenin anahtarı.</summary>
    public required string DescriptionKey { get; init; }

    public string Description => LocalizationService.Instance[DescriptionKey];

    /// <summary>Bu modülün tamamlanacağı faz. 0 = hazır.</summary>
    public int Phase { get; init; }

    public bool IsReady => Phase == 0;

    /// <summary>Themes/Templates.xaml içindeki sayfa şablonunun anahtarı.</summary>
    public string TemplateKey { get; init; } = "PlaceholderPageTemplate";

    /// <summary>Menüde alt bölüme (ayarlar vb.) düşen öğeler.</summary>
    public bool IsFooterItem { get; init; }
}
