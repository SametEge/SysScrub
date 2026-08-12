namespace SysScrub.Core.Cleaning;

/// <summary>Temizliğin nasıl çalışacağı.</summary>
public sealed record CleanOptions
{
    /// <summary>
    /// Hiçbir şey silmeden ne olacağını hesaplar. Şüpheci kullanıcı ve kural yazarı için:
    /// bir kuralın gerçekten neye dokunacağını risk almadan görmenin yolu.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>Karantinadaki dosyaların ne kadar saklanacağı.</summary>
    public TimeSpan QuarantineRetention { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Kilitli dosyalar yeniden başlatmada silinmek üzere işaretlensin mi.</summary>
    public bool ScheduleLockedFilesForReboot { get; init; } = true;

    /// <summary>Boşalan klasörler de kaldırılsın mı (kuralın kendi ayarı da geçerli olmalı).</summary>
    public bool RemoveEmptyDirectories { get; init; } = true;
}

/// <summary>Temizlik ilerlemesi.</summary>
public sealed record CleanProgress
{
    public required string CurrentRule { get; init; }

    public required int Processed { get; init; }

    public required int Total { get; init; }

    public required long BytesFreed { get; init; }

    public double Fraction => Total == 0 ? 0d : (double)Processed / Total;
}

/// <summary>Silinemeyen bir öğe ve sebebi.</summary>
public sealed record CleanFailure(string Path, string RuleId, string Reason);

/// <summary>Temizlik sonucu. Zaman tüneline yazılan kayıt bundan üretilir.</summary>
public sealed record CleanResult
{
    public required Guid RunId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required TimeSpan Duration { get; init; }

    public required long BytesFreed { get; init; }

    public required int Deleted { get; init; }

    public int Quarantined { get; init; }

    public int SentToRecycleBin { get; init; }

    public int ScheduledForReboot { get; init; }

    public int SkippedByGuard { get; init; }

    public required IReadOnlyList<CleanFailure> Failures { get; init; }

    public bool WasDryRun { get; init; }

    /// <summary>Kanıtlı öncesi/sonrası: sistem diskinin gerçek boş alanı.</summary>
    public long FreeSpaceBefore { get; init; }

    public long FreeSpaceAfter { get; init; }

    /// <summary>
    /// Diskten ölçülen gerçek kazanç. Silinen dosyaların toplamından farklı olabilir:
    /// arka planda başka programlar da yazıyor. Kullanıcıya ikisini de gösteriyoruz.
    /// </summary>
    public long MeasuredGain => FreeSpaceAfter - FreeSpaceBefore;

    public bool IsReversible => Quarantined > 0 || SentToRecycleBin > 0;
}
