namespace SysScrub.Core.Formatting;

/// <summary>
/// Süre birimlerinin sözcükleri.
///
/// Motor katmanının dili yok ama süreyi metne çeviren aritmetik burada yaşıyor;
/// sözcükleri dışarıdan almak, aynı hesabı arayüz katmanına kopyalamaktan iyi.
/// Arayüz açılışta ve her dil değişiminde <see cref="DurationText.Words"/>'ü
/// kendi kataloğundan dolduruyor.
/// </summary>
public sealed record DurationWords(
    string Day,
    string Hour,
    string Minute,
    string Second,
    string ShortSecond)
{
    /// <summary>Arayüz olmayan yerlerde (komut satırı, testler) geçerli olan varsayılan.</summary>
    public static DurationWords Turkish { get; } = new("gün", "saat", "dakika", "saniye", "sn");
}
