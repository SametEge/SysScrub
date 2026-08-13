using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace SysScrub.Core.Tests.Localization;

/// <summary>
/// Çeviri kataloglarının tutarlılığı.
///
/// Çeviri hataları koda değil veriye düşüyor: eksik bir yer tutucu ("{0}")
/// çalışma anında biçimlendirmeyi bozar, fazladan bir yer tutucu ise
/// hiç dolmayan bir metin bırakır. Bunlar ancak o dil seçildiğinde görülür —
/// bu yüzden burada, derleme zamanında yakalanıyorlar.
///
/// Kataloglar veri dosyası olduğu için test de dosyaları okuyor.
/// </summary>
public sealed class CatalogConsistencyTests
{
    private const string NeutralCulture = "tr";

    private static readonly Regex Placeholder = new(@"\{\d+\}", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    /// <summary>Depo kökünü bulur; test çalışırken bin klasöründen başlıyoruz.</summary>
    private static string CatalogDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "data", "i18n");

                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("data/i18n bulunamadı.");
        }
    }

    private static Dictionary<string, string> Load(string culture) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(CatalogDirectory, $"{culture}.json")), JsonOptions)
        ?? throw new InvalidOperationException($"{culture}.json okunamadı.");

    private static IEnumerable<string> Cultures =>
        Directory.GetFiles(CatalogDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is { Length: > 0 })!;

    public static TheoryData<string> TranslatedCultures
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (string culture in Cultures.Where(c => c != NeutralCulture))
            {
                data.Add(culture);
            }

            return data;
        }
    }

    [Fact]
    public void AltiDilKatalogduruyor() => Assert.Equal(6, Cultures.Count());

    [Fact]
    public void KaynakKatalogDoluDur() => Assert.True(Load(NeutralCulture).Count > 60);

    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void KatalogGecerliJson(string culture) => Assert.NotEmpty(Load(culture));

    /// <summary>
    /// Her katalog kendi kültür kodunu taşımalı: servis dosya adına değil bu
    /// alana bakıyor.
    /// </summary>
    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void KulturKoduDosyaAdiylaEslesir(string culture) =>
        Assert.Equal(culture, Load(culture)["_culture"]);

    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void DilinKendiAdiVar(string culture) =>
        Assert.False(string.IsNullOrWhiteSpace(Load(culture).GetValueOrDefault("_name")));

    /// <summary>
    /// Yer tutucular birebir aynı olmalı. Çeviride "{0}" düşerse metin hiç
    /// dolmaz; fazladan "{1}" eklenirse biçimlendirme hata verir.
    /// </summary>
    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void YerTutucularKaynaklaAyni(string culture)
    {
        Dictionary<string, string> neutral = Load(NeutralCulture);
        Dictionary<string, string> translated = Load(culture);

        var problems = new List<string>();

        foreach ((string key, string source) in neutral.Where(p => !p.Key.StartsWith('_')))
        {
            if (!translated.TryGetValue(key, out string? target))
            {
                continue;
            }

            string[] expected = Placeholders(source);
            string[] actual = Placeholders(target);

            if (!expected.SequenceEqual(actual))
            {
                problems.Add($"{key}: beklenen [{string.Join(",", expected)}], bulunan [{string.Join(",", actual)}]");
            }
        }

        Assert.True(problems.Count == 0, $"{culture}.json → {string.Join(" | ", problems)}");
    }

    /// <summary>Kaynakta olmayan anahtar çeviride de olmamalı; ölü satır demek.</summary>
    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void FazladanAnahtarYok(string culture)
    {
        Dictionary<string, string> neutral = Load(NeutralCulture);

        string[] extra = Load(culture).Keys
            .Where(key => !key.StartsWith('_') && !neutral.ContainsKey(key))
            .ToArray();

        Assert.True(extra.Length == 0, $"{culture}.json fazladan anahtar: {string.Join(", ", extra)}");
    }

    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void BosCeviriYok(string culture)
    {
        string[] empty = Load(culture)
            .Where(pair => !pair.Key.StartsWith('_') && string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .ToArray();

        Assert.True(empty.Length == 0, $"{culture}.json boş değer: {string.Join(", ", empty)}");
    }

    /// <summary>
    /// İlk izlenim yüzeyi eksiksiz olmalı: kullanıcı anlamadığı bir dilde
    /// "İleri" düğmesi aramamalı.
    /// </summary>
    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void KarsilamaTuruTamCevrilmis(string culture)
    {
        Dictionary<string, string> neutral = Load(NeutralCulture);
        Dictionary<string, string> translated = Load(culture);

        string[] missing = neutral.Keys
            .Where(key => key.StartsWith("Ob_", StringComparison.Ordinal) ||
                          key.StartsWith("Common_", StringComparison.Ordinal) ||
                          key.StartsWith("Nav_", StringComparison.Ordinal))
            .Where(key => !translated.ContainsKey(key))
            .ToArray();

        Assert.True(missing.Length == 0, $"{culture}.json eksik: {string.Join(", ", missing)}");
    }

    private static string[] Placeholders(string text) =>
        Placeholder.Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal).ToArray();
}
