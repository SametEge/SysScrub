using CommunityToolkit.Mvvm.ComponentModel;

namespace SysScrub.App.Localization;

/// <summary>
/// Anahtarı veriden gelen çevrilmiş metin.
///
/// XAML'deki <c>{loc:Str …}</c> uzantısı anahtar sabitken çalışıyor. Menü
/// başlıkları ve liste satırları gibi anahtarın veriyle geldiği yerlerde
/// dönüştürücü kullanmak yetmiyor: dönüştürücü dil değişimini haber alamıyor
/// ve ekranda eski dil kalıyor. Bu sınıf değişimi dinleyip kendini yeniliyor.
/// </summary>
public sealed partial class LocText : ObservableObject
{
    public LocText(string key)
    {
        Key = key;

        // Servis uygulama ömrü boyunca yaşıyor; abonelik de öyle. Nesneler
        // sayıca az (menü, tur satırları) olduğu için ayrıca çözülmüyor.
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    public string Key { get; }

    public string Value => LocalizationService.Instance[Key];

    public override string ToString() => Value;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Value));
}

/// <summary>
/// Dil değişimini dinleyen görünüm modelleri için ortak taban.
///
/// Türetenler <see cref="OnLanguageChanged"/> içinde hangi metinlerin
/// yenileneceğini bildiriyor. Böylece her görünüm modelinde aynı abonelik
/// kodunu tekrarlamak gerekmiyor.
/// </summary>
public abstract partial class LocalizedViewModel : ObservableObject
{
    protected LocalizedViewModel()
    {
        Localization.LanguageChanged += (_, _) => OnLanguageChanged();
    }

    protected static LocalizationService Localization => LocalizationService.Instance;

    /// <summary>Anahtarın çevirisi.</summary>
    protected static string Str(string key) => LocalizationService.Instance[key];

    /// <summary>Biçimlendirilmiş çeviri.</summary>
    protected static string Str(string key, params object?[] arguments) =>
        LocalizationService.Instance.Format(key, arguments);

    /// <summary>Dil değişince yenilenecek metinleri bildirir.</summary>
    protected abstract void OnLanguageChanged();
}
