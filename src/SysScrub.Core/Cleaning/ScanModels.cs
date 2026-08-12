using SysScrub.Core.Rules;

namespace SysScrub.Core.Cleaning;

/// <summary>Taramada bulunan tek bir öğe.</summary>
public sealed record ScanItem
{
    public required string Path { get; init; }

    public required long Bytes { get; init; }

    public required DateTime LastWriteUtc { get; init; }

    /// <summary>
    /// Bu öğenin bulunduğu kuralın çözümlenmiş kökü. Silme anında SafetyGuard'a
    /// aynı kök verilir — böylece denetim, taramadaki bağlamla birebir aynı olur.
    /// </summary>
    public required string AllowedRoot { get; init; }

    /// <summary>Bir dosya değil, özel işleyiciyle temizlenen bir bütün (Geri Dönüşüm Kutusu gibi).</summary>
    public bool IsHandlerItem { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>Tek bir kuralın tarama sonucu.</summary>
public sealed record RuleScanResult
{
    public required CleaningRule Rule { get; init; }

    public required IReadOnlyList<ScanItem> Items { get; init; }

    /// <summary>Kuralın engelleyen süreçlerinden çalışır durumda olanlar.</summary>
    public IReadOnlyList<string> RunningBlockers { get; init; } = [];

    /// <summary>Kuralın hedefi bu makinede yoksa true. Arayüzde gösterilmez.</summary>
    public bool NoTargets { get; init; }

    public long Bytes => Items.Sum(i => i.Bytes);

    public int Count => Items.Count;

    public bool HasFindings => Items.Count > 0;
}

/// <summary>Tam tarama raporu.</summary>
public sealed record ScanReport
{
    public required IReadOnlyList<RuleScanResult> Results { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary>Yönetici hakkı olmadığı için atlanan kural sayısı.</summary>
    public int SkippedForElevation { get; init; }

    public static ScanReport Empty { get; } = new()
    {
        Results = [],
        StartedAt = DateTimeOffset.Now,
        Duration = TimeSpan.Zero
    };

    public IReadOnlyList<RuleScanResult> WithFindings =>
        Results.Where(r => r.HasFindings).ToArray();

    public long TotalBytes => Results.Sum(r => r.Bytes);

    public int TotalCount => Results.Sum(r => r.Count);
}

/// <summary>Tarama ilerlemesi. Belirsiz çubuk yerine gerçek sayılar gösterebilmek için.</summary>
public sealed record ScanProgress
{
    public required string CurrentRule { get; init; }

    public required int CompletedRules { get; init; }

    public required int TotalRules { get; init; }

    public required long BytesFound { get; init; }

    public required int FilesFound { get; init; }

    public double Fraction => TotalRules == 0 ? 0d : (double)CompletedRules / TotalRules;
}

/// <summary>Taramanın kapsamı.</summary>
public sealed record ScanOptions
{
    /// <summary>Taranacak kural kimlikleri. Null ise kuralların varsayılan durumu kullanılır.</summary>
    public IReadOnlySet<string>? EnabledRuleIds { get; init; }

    /// <summary>Yükseltilmiş haklarımız yoksa yönetici gerektiren kurallar atlanır.</summary>
    public bool IsElevated { get; init; } = true;

    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    public bool IsEnabled(CleaningRule rule) =>
        EnabledRuleIds is null ? rule.DefaultEnabled : EnabledRuleIds.Contains(rule.Id);
}
