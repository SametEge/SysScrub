using System.Globalization;

namespace SysScrub.Core.Formatting;

/// <summary>
/// Motor katmanının kullanıcıya görünen metinleri.
///
/// Motorun kendi çeviri altyapısı yok ve olmamalı: kural adları JSON'dan,
/// arayüz metinleri katalogdan geliyor. Ama tarayıcı adları, disk sağlığı
/// yorumları ve hata nedenleri motorda üretiliyor ve doğrudan ekrana çıkıyor.
///
/// Çözüm, sözcükleri dışarıdan almak: arayüz açılışta <see cref="Source"/>'u
/// kendi kataloğuna bağlıyor. Bağlanmadığında ya da anahtar bulunmadığında
/// çağrı yerindeki Türkçe karşılık kullanılıyor — komut satırı ve testler
/// hiçbir şey kurmadan çalışmaya devam ediyor.
/// </summary>
public static class CoreText
{
    /// <summary>Anahtarı çeviren kaynak. Bulunamayan anahtar için null dönmeli.</summary>
    public static Func<string, string?> Source { get; set; } = static _ => null;

    /// <summary>Çevrilmiş metin; yoksa çağrı yerindeki Türkçe karşılık.</summary>
    public static string Get(string key, string turkish) => Source(key) ?? turkish;

    /// <summary>
    /// Biçimlendirilmiş metin. Çeviri yer tutucuları bozuksa uygulama düşmemeli;
    /// böyle bir durumda Türkçe karşılık kullanılıyor.
    /// </summary>
    public static string Format(string key, string turkish, params object?[] arguments)
    {
        string template = Source(key) ?? turkish;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, arguments);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, turkish, arguments);
        }
    }
}
