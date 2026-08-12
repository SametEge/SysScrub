namespace SysScrub.Core.Rules;

/// <summary>Bir kuralın hedeflediği klasör: sembolik kök + altındaki (joker karakterli olabilen) yol.</summary>
public sealed record RuleRoot
{
    public required PathToken Base { get; init; }

    /// <summary>Kökün altındaki göreli yol. Boşsa kökün kendisi hedeflenir.</summary>
    public string? Path { get; init; }

    public override string ToString() => Path is null ? Base.ToString() : $"{Base}/{Path}";
}

/// <summary>
/// Tek bir temizleme kuralı. Kod içinde tanımlanmaz, JSON'dan yüklenir —
/// yeni bir temizlik hedefi eklemek kod değişikliği değil, dosya eklemektir.
/// </summary>
public sealed record CleaningRule
{
    /// <summary>Benzersiz kimlik: "browser.chrome.cache". Ayarlarda ve günlükte bununla anılır.</summary>
    public required string Id { get; init; }

    public required RuleCategory Category { get; init; }

    /// <summary>Arayüzde gösterilen grup başlığı ("Google Chrome", "Windows Update").</summary>
    public required string Group { get; init; }

    public required LocalizedText Name { get; init; }

    /// <summary>"Neden?" düğmesinin gösterdiği açıklama.</summary>
    public LocalizedText? Explanation { get; init; }

    public RiskLevel Risk { get; init; } = RiskLevel.Safe;

    public bool DefaultEnabled { get; init; } = true;

    public bool RequiresAdmin { get; init; }

    public required IReadOnlyList<RuleRoot> Roots { get; init; }

    public IReadOnlyList<GlobPattern> Include { get; init; } = [GlobPattern.MatchAll];

    public IReadOnlyList<GlobPattern> Exclude { get; init; } = [];

    /// <summary>
    /// Bu yaştan genç dosyalara dokunulmaz. Kullanımdaki geçici dosyaların
    /// altından çekilmemek için önemli — 0 değeri "yaş bakma" demektir.
    /// </summary>
    public int MinAgeDays { get; init; }

    public DeleteMode DeleteMode { get; init; } = DeleteMode.Quarantine;

    /// <summary>
    /// Bu süreçler çalışırken kural taranır ama temizlik uyarı verir:
    /// açık bir tarayıcının önbelleğini silmek dosya kilidi hatası üretir.
    /// </summary>
    public IReadOnlyList<string> BlockingProcesses { get; init; } = [];

    /// <summary>Alt klasörlere inilsin mi. Kapalıysa yalnızca kökteki dosyalar taranır.</summary>
    public bool Recursive { get; init; } = true;

    /// <summary>Temizlik sonunda boşalan klasörler de kaldırılsın mı.</summary>
    public bool RemoveEmptyDirectories { get; init; } = true;

    /// <summary>Kuralın kendisi değil, özel bir işleyici çalıştırır (Geri Dönüşüm Kutusu gibi).</summary>
    public string? Handler { get; init; }

    public bool Matches(string relativePath)
    {
        if (Exclude.Count > 0 && GlobPattern.IsMatchAny(Exclude, relativePath))
        {
            return false;
        }

        return Include.Count == 0 || GlobPattern.IsMatchAny(Include, relativePath);
    }

    public override string ToString() => Id;
}
