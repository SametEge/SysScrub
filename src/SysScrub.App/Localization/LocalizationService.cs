using System.Collections.Frozen;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using SysScrub.Core.Formatting;

namespace SysScrub.App.Localization;

/// <summary>Seçilebilir bir dil ve o dilin çeviri durumu.</summary>
public sealed record LanguageOption(string Culture, string NativeName, int CoveragePercent)
{
    /// <summary>Kullanıcı seçimi yerine işletim sisteminin dili kullanılsın.</summary>
    public const string Automatic = "auto";

    public bool IsComplete => CoveragePercent >= 100;
}

/// <summary>
/// Arayüz çevirisi.
///
/// Kaynak biçimi olarak .resx yerine düz JSON seçildi: resx'in XML gövdesi
/// çevirmen için okunmaz ve bir dil eklemek yüzlerce satırlık XML demek. JSON
/// katalog `data/i18n/` altında duruyor; bir dile katkı vermek tek dosya
/// göndermekten ibaret.
///
/// Eksik anahtar hata değil: Türkçe karşılığına düşülüyor. Çeviri parça parça
/// ilerlerken uygulama her adımda çalışır kalıyor ve ekranlar çeviri geldikçe
/// kendiliğinden dönüyor.
///
/// Bağlamalar canlı: dil değişince <see cref="PropertyChanged"/> "Item[]" ile
/// tetikleniyor ve tüm metinler yeniden okunuyor — yeniden başlatma gerekmiyor.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    /// <summary>Diğer diller eksik anahtarlarda buna düşer.</summary>
    public const string NeutralCulture = "tr";

    private const string ResourcePrefix = "SysScrub.App.Localization.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> _catalogs;
    private readonly FrozenDictionary<string, string> _neutral;

    private FrozenDictionary<string, string> _active;

    public LocalizationService()
    {
        _catalogs = LoadCatalogs();
        _neutral = _catalogs.TryGetValue(NeutralCulture, out FrozenDictionary<string, string>? neutral)
            ? neutral
            : FrozenDictionary<string, string>.Empty;

        _active = _neutral;
        Culture = NeutralCulture;

        Languages = _catalogs
            .Select(pair => new LanguageOption(
                pair.Key,
                pair.Value.GetValueOrDefault("_name", pair.Key),
                Coverage(pair.Value)))
            // Kaynak dil başta, kalanlar kapsamı yüksekten düşüğe.
            .OrderByDescending(l => l.Culture == NeutralCulture)
            .ThenByDescending(l => l.CoveragePercent)
            .ThenBy(l => l.NativeName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        // Use() aynı dile geçişte erken dönüyor; başlangıç dili için de bağlansın.
        BindDurationWords();
        BindCoreText();
    }

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Dil değişti.
    ///
    /// "Item[]" bildirimi yalnızca XAML'de sabit anahtarla kurulan bağlamaları
    /// yeniliyor. Anahtarı veriden gelen metinler (menü başlıkları, liste satırları)
    /// ve C# tarafında kurulan cümleler bu olayı dinlemek zorunda — yoksa dil
    /// değişince ekranda eski dil kalıyor.
    /// </summary>
    public event EventHandler? LanguageChanged;

    public IReadOnlyList<LanguageOption> Languages { get; }

    /// <summary>Şu an kullanılan dil.</summary>
    public string Culture { get; private set; }

    /// <summary>Bağlamaların okuduğu dizin. Anahtar yoksa anahtarın kendisi döner.</summary>
    public string this[string key] =>
        _active.TryGetValue(key, out string? value) ? value
        : _neutral.TryGetValue(key, out string? fallback) ? fallback
        : key;

    /// <summary>Biçimlendirilmiş metin. Yanlış argüman sayısı uygulamayı düşürmemeli.</summary>
    public string Format(string key, params object?[] arguments)
    {
        string template = this[key];

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>
    /// İşletim sisteminin dilini kataloglarla eşleştirir.
    ///
    /// Önce tam ad ("zh-Hans"), sonra iki harfli kod ("zh") deneniyor: Windows
    /// "zh-Hans-CN" gibi bölgeli adlar veriyor ve tam eşitlik arayan bir kontrol
    /// hiçbirini bulamazdı.
    /// </summary>
    public string DetectSystemCulture()
    {
        CultureInfo culture = CultureInfo.InstalledUICulture;

        for (CultureInfo? candidate = culture;
             candidate is not null && candidate != CultureInfo.InvariantCulture;
             candidate = candidate.Parent == candidate ? null : candidate.Parent)
        {
            if (_catalogs.ContainsKey(candidate.Name))
            {
                return candidate.Name;
            }
        }

        // "zh-Hans-CN" → "zh-Hans" ana kültür zincirinde yoksa elle deneniyor.
        string[] parts = culture.Name.Split('-');

        for (int take = parts.Length - 1; take >= 1; take--)
        {
            string candidate = string.Join('-', parts, 0, take);

            if (_catalogs.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return NeutralCulture;
    }

    /// <summary>İşletim sisteminin dilinin okunabilir adı; katalogda olmasa bile gösterilir.</summary>
    public string SystemCultureName => CultureInfo.InstalledUICulture.NativeName;

    /// <summary>Dili değiştirir. "auto" verilirse işletim sisteminin dili kullanılır.</summary>
    public void Use(string? culture)
    {
        string wanted = string.IsNullOrWhiteSpace(culture) || culture == LanguageOption.Automatic
            ? DetectSystemCulture()
            : culture;

        if (!_catalogs.TryGetValue(wanted, out FrozenDictionary<string, string>? catalog))
        {
            catalog = _neutral;
            wanted = NeutralCulture;
        }

        if (Culture == wanted)
        {
            return;
        }

        Culture = wanted;
        _active = catalog;

        // Sayı ve tarih biçimleri de dile uymalı; yalnızca metin çevirmek yarım iş.
        var info = CultureInfo.GetCultureInfo(wanted);
        CultureInfo.CurrentCulture = info;
        CultureInfo.CurrentUICulture = info;

        BindDurationWords();
        BindCoreText();

        // "Item[]" tüm dizin bağlamalarını yeniler.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Motor katmanındaki süre biçimlendiricisine birim sözcüklerini verir.
    /// Aritmetik orada, sözcükler burada: "2 saat 19 dakika" cümlesinin hesabı
    /// tek yerde kalıyor ama dili arayüzden geliyor.
    /// </summary>
    private void BindDurationWords() => DurationText.Words = new DurationWords(
        this["Dur_Day"],
        this["Dur_Hour"],
        this["Dur_Minute"],
        this["Dur_Second"],
        this["Dur_SecondShort"]);

    /// <summary>
    /// Motor katmanının ekrana çıkan metinlerini kataloğa bağlar: registry
    /// tarayıcı adları, disk sağlığı yorumları, sürücü durumları ve hata
    /// nedenleri motorda üretiliyor ama kullanıcı onları arayüzde okuyor.
    ///
    /// Bulunmayan anahtarda null dönüyoruz; motor kendi Türkçe karşılığına
    /// düşüyor — arayüzdeki eksik anahtar davranışının aynısı.
    /// </summary>
    private void BindCoreText() => CoreText.Source = key =>
        _active.TryGetValue(key, out string? value) ? value
        : _neutral.TryGetValue(key, out string? fallback) ? fallback
        : null;

    public LanguageOption? Find(string culture) =>
        Languages.FirstOrDefault(l => l.Culture == culture);

    /// <summary>Bir kataloğun kaynak dile göre tamamlanma oranı.</summary>
    private int Coverage(FrozenDictionary<string, string> catalog)
    {
        int total = _neutral.Keys.Count(IsTranslatable);

        if (total == 0)
        {
            return 100;
        }

        int translated = _neutral.Keys.Count(key => IsTranslatable(key) && catalog.ContainsKey(key));

        return (int)Math.Round(translated * 100d / total);
    }

    /// <summary>Alt çizgiyle başlayan anahtarlar üstveri; çeviri sayımına girmezler.</summary>
    private static bool IsTranslatable(string key) => !key.StartsWith('_');

    private static FrozenDictionary<string, FrozenDictionary<string, string>> LoadCatalogs()
    {
        Assembly assembly = typeof(LocalizationService).Assembly;
        var catalogs = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using Stream? stream = assembly.GetManifestResourceStream(resource);

                if (stream is null)
                {
                    continue;
                }

                Dictionary<string, string>? entries =
                    JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOptions);

                if (entries is null || entries.Count == 0)
                {
                    continue;
                }

                string culture = entries.GetValueOrDefault(
                    "_culture",
                    resource[ResourcePrefix.Length..^".json".Length]);

                catalogs[culture] = entries.ToFrozenDictionary(StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                // Bozuk katalog o dili devre dışı bırakır; uygulama düşmez.
            }
        }

        return catalogs.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
