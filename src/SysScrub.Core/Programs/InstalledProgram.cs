using Microsoft.Win32;

namespace SysScrub.Core.Programs;

/// <summary>Programın nereden okunduğu.</summary>
public enum ProgramSource
{
    /// <summary>Klasik masaüstü programı — Uninstall kaydından okunur.</summary>
    Registry,

    /// <summary>Microsoft Store / UWP paketi.</summary>
    Store
}

/// <summary>Kurulu tek bir program.</summary>
public sealed record InstalledProgram
{
    /// <summary>Kaldırma ve seçim işlemlerinde kullanılan kararlı kimlik.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required ProgramSource Source { get; init; }

    public string? Publisher { get; init; }

    public string? Version { get; init; }

    public DateTime? InstallDate { get; init; }

    public string? InstallLocation { get; init; }

    /// <summary>Kaldırıcının komut satırı. Yoksa program buradan kaldırılamaz.</summary>
    public string? UninstallCommand { get; init; }

    /// <summary>Sessiz kaldırma komutu; varsa pencere açılmadan kaldırılır.</summary>
    public string? QuietUninstallCommand { get; init; }

    /// <summary>Kaydın bildirdiği boyut. Çoğu program bunu yazmıyor.</summary>
    public long RegistrySizeBytes { get; init; }

    /// <summary>Kurulum klasörü taranarak ölçülen gerçek boyut; ölçülmediyse null.</summary>
    public long? MeasuredSizeBytes { get; init; }

    /// <summary>
    /// Kaldırıcının kendisi kayıp. Program kayıtlı görünüyor ama kaldırılamıyor —
    /// elle silinmiş bir kurulumun tipik izi.
    /// </summary>
    public bool UninstallerMissing { get; init; }

    /// <summary>Windows'un gizlediği bileşen (çalışma zamanları, yamalar).</summary>
    public bool IsSystemComponent { get; init; }

    public bool Is32Bit { get; init; }

    public bool IsMachineWide { get; init; }

    // ---- kaynağa özel alanlar

    public RegistryHive Hive { get; init; }

    public RegistryView View { get; init; }

    /// <summary>Uninstall altındaki alt anahtar yolu; kaldırma sonrası doğrulama bunu kullanır.</summary>
    public string? RegistryKeyPath { get; init; }

    /// <summary>Store paketlerinde tam paket adı; kaldırma bununla yapılır.</summary>
    public string? PackageFullName { get; init; }

    /// <summary>Ölçüm varsa o, yoksa kaydın bildirdiği boyut.</summary>
    public long SizeBytes => MeasuredSizeBytes ?? RegistrySizeBytes;

    /// <summary>Boyut hiç bilinmiyorsa "—" gösterilecek; sıfır yazmak yanıltıcı olurdu.</summary>
    public bool HasSize => SizeBytes > 0;

    public bool CanUninstall => Source == ProgramSource.Store
        ? PackageFullName is not null
        : !string.IsNullOrWhiteSpace(UninstallCommand) && !UninstallerMissing;

    public bool SupportsQuietUninstall =>
        Source == ProgramSource.Store || !string.IsNullOrWhiteSpace(QuietUninstallCommand);

    public string SourceLabel => Source switch
    {
        ProgramSource.Store => "Microsoft Store",
        _ => IsMachineWide ? "Tüm kullanıcılar" : "Bu kullanıcı"
    };

    /// <summary>Kurulum klasörü taranabilir mi — boyut ölçümü ve artık taraması için.</summary>
    public bool HasScannableLocation =>
        !string.IsNullOrWhiteSpace(InstallLocation) && Directory.Exists(InstallLocation);

    public override string ToString() => $"{Name} {Version}".TrimEnd();
}

/// <summary>Kurulu program envanteri.</summary>
public sealed record ProgramInventoryReport
{
    public required IReadOnlyList<InstalledProgram> Programs { get; init; }

    public required TimeSpan Duration { get; init; }

    public static ProgramInventoryReport Empty { get; } = new() { Programs = [], Duration = TimeSpan.Zero };

    public int VisibleCount => Programs.Count(p => !p.IsSystemComponent);

    public int ComponentCount => Programs.Count(p => p.IsSystemComponent);

    public int StoreCount => Programs.Count(p => p.Source == ProgramSource.Store);

    /// <summary>Bilinen boyutların toplamı. Boyutu okunamayan programlar dahil değil.</summary>
    public long KnownSizeBytes => Programs.Where(p => !p.IsSystemComponent).Sum(p => p.SizeBytes);
}
