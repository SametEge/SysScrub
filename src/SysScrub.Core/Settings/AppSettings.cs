using System.Text.Json.Serialization;

namespace SysScrub.Core.Settings;

/// <summary>Tema tercihi. Arayüz katmanı bunu kendi türüne çeviriyor.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>
/// Kullanıcı ayarları.
///
/// Varsayılanlar bilerek muhafazakâr: yeni kurulumda hiçbir otomatik silme
/// açık değil ve karantina bir hafta saklanıyor. Kullanıcı isterse açar.
/// </summary>
public sealed record AppSettings
{
    [JsonPropertyName("theme")]
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>
    /// Arayüz dili. "auto" işletim sisteminin dilini kullanır — yeni kurulumda
    /// kullanıcı hiçbir şey seçmeden kendi dilini görüyor.
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; init; } = AutomaticLanguage;

    /// <summary>Karşılama turu tamamlandı mı; ilk açılışta gösterilmesini bu belirliyor.</summary>
    [JsonPropertyName("tourCompleted")]
    public bool TourCompleted { get; init; }

    public const string AutomaticLanguage = "auto";

    /// <summary>Karantinadaki dosyaların kaç gün saklanacağı.</summary>
    [JsonPropertyName("quarantineRetentionDays")]
    public int QuarantineRetentionDays { get; init; } = DefaultRetentionDays;

    /// <summary>
    /// Registry temizliği ve sürücü kurulumu öncesi sistem geri yükleme noktası.
    /// Varsayılan açık: ikinci güvenlik ağını kullanıcının kapatması gerekir,
    /// açması değil.
    /// </summary>
    [JsonPropertyName("createRestorePoint")]
    public bool CreateRestorePoint { get; init; } = true;

    /// <summary>Haftalık otomatik temizlik görevi kayıtlı mı.</summary>
    [JsonPropertyName("scheduledCleanup")]
    public bool ScheduledCleanup { get; init; }

    /// <summary>Zamanlanmış temizliğin çalışacağı saat (0–23).</summary>
    [JsonPropertyName("scheduledHour")]
    public int ScheduledHour { get; init; } = DefaultScheduledHour;

    /// <summary>Günlük dosyalarının kaç gün saklanacağı.</summary>
    [JsonPropertyName("logRetentionDays")]
    public int LogRetentionDays { get; init; } = DefaultLogRetentionDays;

    /// <summary>
    /// Açılışta GitHub'daki yayınlara bakılsın mı. Varsayılan açık: dağıtım
    /// kanalı GitHub olan bir uygulamada bu kapalıysa herkes ilk indirdiği
    /// sürümde kalır. Denetim yalnızca sürüm numarası okur, hiçbir şey göndermez.
    /// </summary>
    [JsonPropertyName("autoCheckUpdates")]
    public bool AutoCheckUpdates { get; init; } = true;

    /// <summary>Son otomatik denetimin zamanı; günde birden fazla sorgulamamak için.</summary>
    [JsonPropertyName("lastUpdateCheck")]
    public DateTimeOffset? LastUpdateCheck { get; init; }

    public const int DefaultRetentionDays = 7;
    public const int MinimumRetentionDays = 1;
    public const int MaximumRetentionDays = 90;

    public const int DefaultScheduledHour = 3;
    public const int DefaultLogRetentionDays = 14;

    public static AppSettings Default { get; } = new();

    /// <summary>
    /// Elle düzenlenmiş ya da bozulmuş dosyadan gelen değerleri sınırlara çeker.
    /// Sıfır gün saklama, karantinayı anlamsız kılardı.
    /// </summary>
    public AppSettings Normalized() => this with
    {
        QuarantineRetentionDays = Math.Clamp(QuarantineRetentionDays, MinimumRetentionDays, MaximumRetentionDays),
        ScheduledHour = Math.Clamp(ScheduledHour, 0, 23),
        LogRetentionDays = Math.Clamp(LogRetentionDays, 1, 365),
        Theme = Enum.IsDefined(Theme) ? Theme : ThemePreference.System,
        Language = string.IsNullOrWhiteSpace(Language) ? AutomaticLanguage : Language.Trim()
    };

    public TimeSpan QuarantineRetention => TimeSpan.FromDays(QuarantineRetentionDays);
}
