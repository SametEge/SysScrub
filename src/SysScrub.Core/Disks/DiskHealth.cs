using SysScrub.Core.Formatting;

namespace SysScrub.Core.Disks;

/// <summary>
/// Diskin sağlık durumu. CrystalDiskInfo'nun modeliyle aynı dört seviye —
/// yaygın olduğu ve kullanıcıların tanıdığı bir ölçek olduğu için.
/// </summary>
public enum DiskHealthStatus
{
    /// <summary>Okunamadı. "Sorun yok" demek değil; bilmiyoruz demek.</summary>
    Unknown,

    Good,

    Caution,

    Bad
}

/// <summary>SMART verisinin hangi yoldan okunduğu.</summary>
public enum SmartAccessMethod
{
    /// <summary>Hiçbir yol işe yaramadı.</summary>
    None,

    /// <summary>NVMe sağlık günlüğü (log sayfası 0x02).</summary>
    Nvme,

    /// <summary>ATA SMART öznitelikleri.</summary>
    Ata
}

/// <summary>Tek bir S.M.A.R.T. özniteliği (ATA diskler).</summary>
public sealed record SmartAttribute
{
    public required byte Id { get; init; }

    /// <summary>Öznitelik tablosundan gelen ad; tabloda yoksa "Bilinmeyen öznitelik".</summary>
    public required string Name { get; init; }

    /// <summary>Sade Türkçe açıklama; kullanıcı ham sayıya bakmak zorunda kalmasın.</summary>
    public string? Description { get; init; }

    public required byte Current { get; init; }

    public required byte Worst { get; init; }

    public required byte Threshold { get; init; }

    /// <summary>48 bitlik ham değer. Anlamı özniteliğe göre değişiyor.</summary>
    public required long Raw { get; init; }

    /// <summary>Eşiğin altına düşen öznitelik üreticiye göre arıza habercisi.</summary>
    public bool IsBelowThreshold => Threshold > 0 && Current <= Threshold;

    /// <summary>Ham değeri sıfırdan büyük olduğunda sorun anlamına gelen öznitelikler.</summary>
    public bool IsCritical { get; init; }

    public DiskHealthStatus Status
    {
        get
        {
            if (IsBelowThreshold)
            {
                return DiskHealthStatus.Bad;
            }

            return IsCritical && Raw > 0 ? DiskHealthStatus.Caution : DiskHealthStatus.Good;
        }
    }

    public string RawHex => $"{Raw:X12}";
}

/// <summary>NVMe sağlık günlüğünden okunan değerler.</summary>
public sealed record NvmeHealth
{
    /// <summary>Denetleyicinin bildirdiği kritik uyarı bitleri; sıfır olmalı.</summary>
    public required byte CriticalWarning { get; init; }

    /// <summary>Bileşik sıcaklık (santigrat).</summary>
    public required int TemperatureCelsius { get; init; }

    /// <summary>Kalan yedek blok yüzdesi.</summary>
    public required byte AvailableSpare { get; init; }

    /// <summary>Üreticinin uyarı eşiği; yedek bunun altına düşerse disk riskli.</summary>
    public required byte AvailableSpareThreshold { get; init; }

    /// <summary>Tüketilen ömür yüzdesi. 100'ü geçebilir; üretici garantisi biter.</summary>
    public required byte PercentageUsed { get; init; }

    public required long DataUnitsRead { get; init; }

    public required long DataUnitsWritten { get; init; }

    public required long PowerCycles { get; init; }

    public required long PowerOnHours { get; init; }

    public required long UnsafeShutdowns { get; init; }

    /// <summary>Düzeltilemeyen veri hataları; sıfırdan büyükse veri kaybı olmuş demek.</summary>
    public required long MediaErrors { get; init; }

    public required long ErrorLogEntries { get; init; }

    /// <summary>Ayrı sıcaklık sensörleri (santigrat); bildirmeyen diskte boş.</summary>
    public IReadOnlyList<int> SensorsCelsius { get; init; } = [];

    /// <summary>
    /// Yazılan veri. NVMe standardı 1000 × 512 bayt birimiyle sayıyor;
    /// doğrudan bayta çevirmek yaygın bir hata.
    /// </summary>
    public long BytesWritten => DataUnitsWritten * 512_000;

    public long BytesRead => DataUnitsRead * 512_000;

    /// <summary>Kritik uyarı bitlerinin okunabilir karşılığı.</summary>
    public IReadOnlyList<string> CriticalWarnings
    {
        get
        {
            if (CriticalWarning == 0)
            {
                return [];
            }

            var warnings = new List<string>();

            if ((CriticalWarning & 0x01) != 0)
            {
                warnings.Add(CoreText.Get("Dh_W_Spare", "Yedek blok kapasitesi eşiğin altına düştü"));
            }

            if ((CriticalWarning & 0x02) != 0)
            {
                warnings.Add(CoreText.Get("Dh_W_Temperature", "Sıcaklık güvenli aralığın dışında"));
            }

            if ((CriticalWarning & 0x04) != 0)
            {
                warnings.Add(CoreText.Get("Dh_W_Reliability", "Güvenilirlik düştü; disk arızalanmak üzere olabilir"));
            }

            if ((CriticalWarning & 0x08) != 0)
            {
                warnings.Add(CoreText.Get("Dh_W_ReadOnly", "Disk salt okunur moda geçti"));
            }

            if ((CriticalWarning & 0x10) != 0)
            {
                warnings.Add(CoreText.Get("Dh_W_Volatile", "Yedek belleğin kalıcı kaydı başarısız"));
            }

            return warnings;
        }
    }
}

/// <summary>Tek bir fiziksel disk.</summary>
public sealed record DiskInfo
{
    /// <summary>\\.\PhysicalDriveN numarası.</summary>
    public required int Index { get; init; }

    public required string Model { get; init; }

    public string? SerialNumber { get; init; }

    public string? FirmwareRevision { get; init; }

    /// <summary>Toplam kapasite; okunamazsa 0.</summary>
    public long CapacityBytes { get; init; }

    public required string BusType { get; init; }

    public bool IsSolidState { get; init; }

    public bool IsRemovable { get; init; }

    public required SmartAccessMethod AccessMethod { get; init; }

    /// <summary>SMART okunamadıysa nedeni; kullanıcıya olduğu gibi gösterilir.</summary>
    public string? AccessMessage { get; init; }

    public NvmeHealth? Nvme { get; init; }

    public IReadOnlyList<SmartAttribute> Attributes { get; init; } = [];

    public required DiskHealthStatus Status { get; init; }

    /// <summary>Durumun tek cümlelik gerekçesi. Ham sayı değil, ne anlama geldiği.</summary>
    public required string StatusReason { get; init; }

    /// <summary>Kalan ömür yüzdesi; hesaplanamıyorsa null.</summary>
    public int? HealthPercent { get; init; }

    public int? TemperatureCelsius => Nvme?.TemperatureCelsius ?? AttributeTemperature();

    public long? PowerOnHours => Nvme?.PowerOnHours ?? RawOf(0x09);

    public long? PowerCycles => Nvme?.PowerCycles ?? RawOf(0x0C);

    public long? BytesWritten => Nvme?.BytesWritten;

    public bool HasSmartData => AccessMethod != SmartAccessMethod.None;

    public string CapacityLabel => CapacityBytes > 0 ? ByteSize.Format(CapacityBytes) : "—";

    public string UptimeLabel => PowerOnHours is { } hours && hours > 0
        ? DurationText.Humanize(TimeSpan.FromHours(hours))
        : string.Empty;

    /// <summary>Sıcaklığı bildiren ATA öznitelikleri; ham değerin alt baytı santigrat.</summary>
    private int? AttributeTemperature()
    {
        foreach (byte id in (byte[])[0xC2, 0xBE])
        {
            if (Attributes.FirstOrDefault(a => a.Id == id) is { } attribute)
            {
                return (int)(attribute.Raw & 0xFF);
            }
        }

        return null;
    }

    private long? RawOf(byte id) => Attributes.FirstOrDefault(a => a.Id == id)?.Raw;
}

/// <summary>Tüm disklerin sağlık raporu.</summary>
public sealed record DiskHealthReport
{
    public required IReadOnlyList<DiskInfo> Disks { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary>Yönetici hakkı olmadan SMART okunamıyor; kullanıcıya bunu söylemek gerekiyor.</summary>
    public bool IsElevated { get; init; }

    public static DiskHealthReport Empty { get; } = new() { Disks = [], Duration = TimeSpan.Zero };

    public int ReadableCount => Disks.Count(d => d.HasSmartData);

    /// <summary>En kötü durum: bir disk kötüyse rapor kötüdür.</summary>
    public DiskHealthStatus WorstStatus => Disks.Count == 0
        ? DiskHealthStatus.Unknown
        : Disks.Max(d => d.Status);
}
