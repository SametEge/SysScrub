using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace SysScrub.Core.Tests.Localization;

/// <summary>
/// Çeviri kataloglarının birbirini tutmasını denetler.
///
/// Bu testler iki kez yaşanan bir hatayı kilitliyor: arayüze yeni bir cümle
/// eklenip yalnızca Türkçe'si yazıldığında, o dili seçen kullanıcı ekranın bir
/// kısmını Türkçe görüyordu. Eksik anahtar çalışma zamanında Türkçe'ye düşüyor —
/// yani uygulama çökmüyor, sessizce yarım kalıyor. Sessiz hatayı ancak test
/// yakalar.
/// </summary>
public class CatalogParityTests
{
    private const string Reference = "tr";

    /// <summary>Katalog dosyaları derleme çıktısına kopyalanıyor (csproj'a bakınız).</summary>
    private static string Directory => Path.Combine(AppContext.BaseDirectory, "i18n");

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Catalogs =
        new(Load);

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();

        foreach (string file in System.IO.Directory.GetFiles(Directory, "*.json"))
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(file))!;

            result[Path.GetFileNameWithoutExtension(file)] = values;
        }

        return result;
    }

    [Fact]
    public void Alti_dil_de_yerinde()
    {
        Assert.Equal(
            ["de", "en", "ja", "ko", "tr", "zh-Hans"],
            Catalogs.Value.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>
    /// Her katalogda aynı anahtarlar olmalı. Eksik anahtar = o dilde Türkçe
    /// görünen bir ekran; fazla anahtar = kullanılmayan çeviri.
    /// </summary>
    [Fact]
    public void Butun_kataloglar_ayni_anahtarlari_tasiyor()
    {
        var reference = Catalogs.Value[Reference].Keys.ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach ((string culture, IReadOnlyDictionary<string, string> values) in Catalogs.Value)
        {
            if (culture == Reference)
            {
                continue;
            }

            string[] missing = [.. reference.Except(values.Keys).OrderBy(k => k, StringComparer.Ordinal)];
            string[] extra = [.. values.Keys.Except(reference).OrderBy(k => k, StringComparer.Ordinal)];

            if (missing.Length > 0)
            {
                problems.Add($"{culture} eksik: {string.Join(", ", missing)}");
            }

            if (extra.Length > 0)
            {
                problems.Add($"{culture} fazla: {string.Join(", ", extra)}");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Bos_ceviri_yok()
    {
        var problems = new List<string>();

        foreach ((string culture, IReadOnlyDictionary<string, string> values) in Catalogs.Value)
        {
            problems.AddRange(values
                .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{culture}/{pair.Key}"));
        }

        Assert.Empty(problems);
    }

    /// <summary>
    /// Yer tutucular dile göre değişmemeli: "{0}" bekleyen bir cümlenin
    /// çevirisinde yer tutucu yoksa değer ekrana hiç çıkmaz, fazlaysa
    /// biçimlendirme hatası verir ve metin ham kalır.
    /// </summary>
    [Fact]
    public void Yer_tutucular_her_dilde_ayni()
    {
        var placeholder = new Regex(@"\{(\d+)\}");
        IReadOnlyDictionary<string, string> reference = Catalogs.Value[Reference];
        var problems = new List<string>();

        foreach ((string culture, IReadOnlyDictionary<string, string> values) in Catalogs.Value)
        {
            if (culture == Reference)
            {
                continue;
            }

            foreach ((string key, string turkish) in reference)
            {
                if (!values.TryGetValue(key, out string? translated))
                {
                    continue;
                }

                var expected = placeholder.Matches(turkish).Select(m => m.Groups[1].Value).ToHashSet();
                var actual = placeholder.Matches(translated).Select(m => m.Groups[1].Value).ToHashSet();

                if (!expected.SetEquals(actual))
                {
                    problems.Add(
                        $"{culture}/{key}: beklenen {{{string.Join(",", expected.Order())}}}, " +
                        $"bulunan {{{string.Join(",", actual.Order())}}}");
                }
            }
        }

        Assert.Empty(problems);
    }
}
