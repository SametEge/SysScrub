using SysScrub.Core.Formatting;
namespace SysScrub.Core.Software;

/// <summary>winget'in bildirdiği tek bir güncellenebilir program.</summary>
public sealed record SoftwareUpdate
{
    public required string Name { get; init; }

    /// <summary>winget paket kimliği: güncelleme bununla yapılır, adla değil.</summary>
    public required string Id { get; init; }

    public required string InstalledVersion { get; init; }

    public required string AvailableVersion { get; init; }

    /// <summary>winget, msstore ya da özel bir kaynak.</summary>
    public required string Source { get; init; }

    /// <summary>
    /// Kurulu sürüm okunamamış. winget bunları "Unknown" diye işaretliyor;
    /// güncelleme yine yapılabilir ama neyin üzerine yazılacağı belirsiz.
    /// </summary>
    public bool IsInstalledVersionUnknown =>
        InstalledVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
        InstalledVersion.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase);

    public bool IsFromStore => Source.Equals("msstore", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Name} {InstalledVersion} → {AvailableVersion}";
}

public enum WingetOutcome
{
    Completed,

    /// <summary>winget bu sistemde bulunamadı (Uygulama Yükleyici kurulu değil).</summary>
    NotInstalled,

    Failed
}

public sealed record SoftwareUpdateList
{
    public required WingetOutcome Outcome { get; init; }

    public IReadOnlyList<SoftwareUpdate> Updates { get; init; } = [];

    public string? Message { get; init; }

    public TimeSpan Duration { get; init; }

    public static SoftwareUpdateList Empty { get; } = new() { Outcome = WingetOutcome.Completed };

    public string Describe() => Outcome switch
    {
        WingetOutcome.Completed when Updates.Count == 0 =>
            CoreText.Get("Sw_None", "Güncellenecek program yok. Hepsi güncel."),
        WingetOutcome.Completed =>
            CoreText.Format("Sw_Found", "{0} programın yeni sürümü var.", Updates.Count),
        WingetOutcome.NotInstalled =>
            CoreText.Get("Sw_NoWinget", "winget bu sistemde bulunamadı. Microsoft Store'dan \"Uygulama Yükleyici\" kurulunca çalışır."),
        _ => Message ?? CoreText.Get("Sw_ListFailed", "Program listesi alınamadı.")
    };
}

/// <summary>Tek bir paketin güncellenme sonucu.</summary>
public sealed record SoftwareUpgradeResult(string PackageId, bool Succeeded, string? Message)
{
    public string Describe() => Succeeded
        ? CoreText.Get("Sw_Done", "Güncellendi.")
        : Message ?? CoreText.Get("Sw_Failed", "Güncellenemedi.");
}
