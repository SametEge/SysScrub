using SysScrub.Core.Formatting;

namespace SysScrub.Core.Disks;

/// <summary>
/// Sağlık durumunu hesaplar.
///
/// Bu sınıfın işi ham sayıyı karara çevirmek. İki kural var:
///
///   Bilmediğimize "iyi" demiyoruz. Veri okunamadıysa durum Bilinmiyor kalır;
///   yeşil rozet göstermek kullanıcıyı yanlış güvene sokar.
///
///   Kararın gerekçesi hep yazılıyor. "Dikkat" demek yetmiyor, neden dikkat
///   dendiği tek cümleyle söyleniyor.
/// </summary>
public static class DiskHealthEvaluator
{
    /// <summary>Tüketilen ömür bu yüzdeyi geçince uyarıyoruz.</summary>
    private const int UsedLifeCaution = 80;

    /// <summary>Üretici garantisinin bittiği nokta.</summary>
    private const int UsedLifeBad = 100;

    /// <summary>SSD için uzun vadede ömrü kısaltan sıcaklık.</summary>
    private const int SsdTemperatureCaution = 70;

    private const int TemperatureBad = 80;

    public static (DiskHealthStatus Status, string Reason, int? HealthPercent) Evaluate(NvmeHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);

        // Kalan ömür doğrudan standarttan geliyor; tahmin değil.
        int healthPercent = Math.Clamp(100 - health.PercentageUsed, 0, 100);

        // Denetleyicinin kendi uyarısı her şeyin önünde: diski en iyi o biliyor.
        if (health.CriticalWarnings.Count > 0)
        {
            return (DiskHealthStatus.Bad, health.CriticalWarnings[0], healthPercent);
        }

        if (health.MediaErrors > 0)
        {
            return (DiskHealthStatus.Bad,
                CoreText.Format("Dh_Ev_MediaErrors",
                    "{0} düzeltilemeyen veri hatası var. Verilerinizi hemen yedekleyin.",
                    health.MediaErrors.ToString("N0")),
                healthPercent);
        }

        if (health.AvailableSpareThreshold > 0 && health.AvailableSpare <= health.AvailableSpareThreshold)
        {
            return (DiskHealthStatus.Bad,
                CoreText.Format("Dh_Ev_SpareLow",
                    "Yedek blok kapasitesi %{0} — üreticinin eşiği olan %{1} seviyesine indi.",
                    health.AvailableSpare, health.AvailableSpareThreshold),
                healthPercent);
        }

        if (health.PercentageUsed >= UsedLifeBad)
        {
            return (DiskHealthStatus.Caution,
                CoreText.Get("Dh_Ev_LifeSpent",
                    "Üreticinin öngördüğü yazma ömrü doldu. Disk çalışmaya devam edebilir " +
                    "ama artık yedeksiz bırakılmamalı."),
                healthPercent);
        }

        if (health.TemperatureCelsius >= TemperatureBad)
        {
            return (DiskHealthStatus.Bad,
                CoreText.Format("Dh_Ev_TooHot",
                    "Sıcaklık {0} °C. Bu seviyede disk kendini yavaşlatır ve ömrü kısalır.",
                    health.TemperatureCelsius),
                healthPercent);
        }

        if (health.PercentageUsed >= UsedLifeCaution)
        {
            return (DiskHealthStatus.Caution,
                CoreText.Format("Dh_Ev_LifeHigh",
                    "Yazma ömrünün %{0}'i tüketilmiş. Yakın zamanda değişim planlayın.",
                    health.PercentageUsed),
                healthPercent);
        }

        if (health.TemperatureCelsius >= SsdTemperatureCaution)
        {
            return (DiskHealthStatus.Caution,
                CoreText.Format("Dh_Ev_Warm",
                    "Sıcaklık {0} °C — uzun vadede ömrü kısaltan seviyede. Hava akışını kontrol edin.",
                    health.TemperatureCelsius),
                healthPercent);
        }

        return (DiskHealthStatus.Good, DescribeGood(health), healthPercent);
    }

    private static string DescribeGood(NvmeHealth health)
    {
        var parts = new List<string> { CoreText.Get("Dh_Ev_NoIssue", "Sorun bildiren bir değer yok") };

        if (health.PercentageUsed > 0)
        {
            parts.Add(CoreText.Format("Dh_Ev_LifeUsed",
                "yazma ömrünün %{0}'i tüketilmiş", health.PercentageUsed));
        }
        else
        {
            parts.Add(CoreText.Get("Dh_Ev_LifeFresh", "yazma ömrü neredeyse hiç tüketilmemiş"));
        }

        if (health.TemperatureCelsius > 0)
        {
            parts.Add(CoreText.Format("Dh_Ev_Temp", "sıcaklık {0} °C", health.TemperatureCelsius));
        }

        return string.Join(", ", parts) + ".";
    }

    /// <summary>ATA disklerde karar özniteliklerden çıkıyor.</summary>
    public static (DiskHealthStatus Status, string Reason, int? HealthPercent) Evaluate(
        IReadOnlyList<SmartAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (attributes.Count == 0)
        {
            return (DiskHealthStatus.Unknown,
                CoreText.Get("Dh_E_SmartUnreadable", "S.M.A.R.T. verisi okunamadı."), null);
        }

        int? healthPercent = LifeLeft(attributes);

        // Eşiğin altına düşmüş öznitelik üreticinin kendi arıza tanımı.
        if (attributes.FirstOrDefault(a => a.IsBelowThreshold) is { } failing)
        {
            return (DiskHealthStatus.Bad,
                CoreText.Format("Dh_Ev_BelowThreshold",
                    "\"{0}\" üreticinin belirlediği eşiğin altına düştü ({1} ≤ {2}). " +
                    "Verilerinizi hemen yedekleyin.",
                    failing.Name, failing.Current, failing.Threshold),
                healthPercent);
        }

        // Okunamayan sektör veri kaybı demek; bekleyen sektör onun habercisi.
        if (attributes.FirstOrDefault(a => a.Id == 0xC6 && a.Raw > 0) is { } uncorrectable)
        {
            return (DiskHealthStatus.Bad,
                CoreText.Format("Dh_Ev_Uncorrectable",
                    "{0} sektör hiçbir şekilde okunamıyor. Veri kaybı olmuş olabilir.",
                    uncorrectable.Raw.ToString("N0")),
                healthPercent);
        }

        if (attributes.FirstOrDefault(a => a.Id == 0xC5 && a.Raw > 0) is { } pending)
        {
            return (DiskHealthStatus.Caution,
                CoreText.Format("Dh_Ev_Pending",
                    "{0} sektör okunmakta zorlanıyor. Bu sayı artıyorsa disk arızalanmak üzere; " +
                    "şimdi yedek alın.",
                    pending.Raw.ToString("N0")),
                healthPercent);
        }

        if (attributes.FirstOrDefault(a => a.Id == 0x05 && a.Raw > 0) is { } reallocated)
        {
            return (DiskHealthStatus.Caution,
                CoreText.Format("Dh_Ev_Reallocated",
                    "{0} sektör bozulduğu için yedeğiyle değiştirilmiş. " +
                    "Sayı sabit kaldığı sürece disk kullanılabilir.",
                    reallocated.Raw.ToString("N0")),
                healthPercent);
        }

        SmartAttribute[] critical = attributes.Where(a => a.IsCritical && a.Raw > 0).ToArray();

        if (critical.Length > 0)
        {
            return (DiskHealthStatus.Caution,
                CoreText.Format("Dh_Ev_Critical", "\"{0}\" sıfırdan büyük ({1}). {2}",
                    critical[0].Name,
                    critical[0].Raw.ToString("N0"),
                    critical[0].Id is 0xC7 or 0xB7
                        ? CoreText.Get("Dh_Ev_CableHint",
                            "Genelde veri kablosu sorunudur, diskin kendisi değil.")
                        : CoreText.Get("Dh_Ev_WatchHint", "İzlemeye değer.")),
                healthPercent);
        }

        return (DiskHealthStatus.Good,
            CoreText.Get("Dh_Ev_NoBadAttribute", "Sorun bildiren bir öznitelik yok."), healthPercent);
    }

    /// <summary>SSD kalan ömrünü bildiren öznitelikler; ilki bulunan kullanılır.</summary>
    private static int? LifeLeft(IReadOnlyList<SmartAttribute> attributes)
    {
        // Üreticiler kalan ömrü farklı kimliklerde tutuyor. Hepsinde "güncel değer"
        // yüzde olarak yazılıyor, ham değer değil.
        foreach (byte id in (byte[])[0xE7, 0xE9, 0xB1, 0xCA, 0xAD])
        {
            if (attributes.FirstOrDefault(a => a.Id == id) is { } attribute && attribute.Current <= 100)
            {
                return attribute.Current;
            }
        }

        return null;
    }
}
