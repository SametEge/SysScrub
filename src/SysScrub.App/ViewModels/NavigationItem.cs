namespace SysScrub.App.ViewModels;

/// <summary>
/// Yan menüdeki bir modül. Faz numarası, o modülün henüz gelmediğini dürüstçe göstermek için var —
/// kullanıcı boş bir ekranla karşılaşıp uygulamanın bozuk olduğunu düşünmesin.
/// </summary>
public sealed class NavigationItem
{
    public required string Title { get; init; }

    /// <summary>Themes/Icons.xaml içindeki Geometry anahtarı.</summary>
    public required string IconKey { get; init; }

    public required string Description { get; init; }

    /// <summary>Bu modülün tamamlanacağı faz. 0 = hazır.</summary>
    public int Phase { get; init; }

    public bool IsReady => Phase == 0;

    /// <summary>Themes/Templates.xaml içindeki sayfa şablonunun anahtarı.</summary>
    public string TemplateKey { get; init; } = "PlaceholderPageTemplate";

    /// <summary>Menüde alt bölüme (ayarlar vb.) düşen öğeler.</summary>
    public bool IsFooterItem { get; init; }
}
