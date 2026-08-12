using System.Globalization;

namespace SysScrub.Core.Rules;

/// <summary>
/// Kural adları ve açıklamaları kural dosyasının içinde çok dilli tutulur.
/// Böylece yeni bir kural eklemek için .resx dosyalarına dokunmak gerekmez.
///
/// Çözümleme sırası: tam kültür (tr-TR) → ana dil (tr) → Türkçe → İngilizce → eldeki ilk değer.
/// </summary>
public sealed class LocalizedText
{
    private const string FallbackLanguage = "tr";
    private const string SecondFallbackLanguage = "en";

    private readonly IReadOnlyDictionary<string, string> _values;

    public LocalizedText(IReadOnlyDictionary<string, string> values) =>
        _values = values ?? throw new ArgumentNullException(nameof(values));

    /// <summary>Tek dilli kısayol — kullanıcı kurallarında sadece düz metin yazılabilsin diye.</summary>
    public static LocalizedText FromSingle(string text) =>
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [FallbackLanguage] = text });

    public bool IsEmpty => _values.Count == 0;

    public string Resolve(CultureInfo? culture = null)
    {
        if (_values.Count == 0)
        {
            return string.Empty;
        }

        culture ??= CultureInfo.CurrentUICulture;

        if (_values.TryGetValue(culture.Name, out string? exact))
        {
            return exact;
        }

        if (_values.TryGetValue(culture.TwoLetterISOLanguageName, out string? language))
        {
            return language;
        }

        if (_values.TryGetValue(FallbackLanguage, out string? turkish))
        {
            return turkish;
        }

        return _values.TryGetValue(SecondFallbackLanguage, out string? english)
            ? english
            : _values.Values.First();
    }

    public override string ToString() => Resolve();
}
