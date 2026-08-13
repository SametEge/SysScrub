namespace SysScrub.Core.Machine;

/// <summary>Panelin gösterdiği anlık sistem durumu. Tamamen salt-okunur veriden oluşur.</summary>
public sealed record SystemSnapshot
{
    /// <summary>"Windows 11 Pro 25H2" — derleme numarası ayrı, cümleyi arayüz kuruyor.</summary>
    public required string OperatingSystem { get; init; }

    /// <summary>Windows derleme numarası; okunamazsa boş.</summary>
    public string WindowsBuild { get; init; } = string.Empty;

    public required string MachineName { get; init; }

    public required TimeSpan Uptime { get; init; }

    public required bool IsElevated { get; init; }

    public required IReadOnlyList<DriveSnapshot> Drives { get; init; }

    public required MemorySnapshot Memory { get; init; }

    /// <summary>Windows'un kurulu olduğu sürücü. Temizliğin çoğu burada kazanç sağlar.</summary>
    public DriveSnapshot? SystemDrive =>
        Drives.FirstOrDefault(d => string.Equals(
            d.Name,
            Path.GetPathRoot(Environment.SystemDirectory),
            StringComparison.OrdinalIgnoreCase));
}

public sealed record DriveSnapshot
{
    public required string Name { get; init; }

    public required string Label { get; init; }

    public required string Format { get; init; }

    public required long TotalBytes { get; init; }

    public required long FreeBytes { get; init; }

    public long UsedBytes => TotalBytes - FreeBytes;

    public double UsedRatio => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0d;

    /// <summary>SSD'lerde %90 üzeri doluluk yazma başarımını gözle görülür düşürür.</summary>
    public bool IsCriticallyFull => UsedRatio >= 0.90d;
}

public sealed record MemorySnapshot
{
    public required ulong TotalBytes { get; init; }

    public required ulong AvailableBytes { get; init; }

    public ulong UsedBytes => TotalBytes - AvailableBytes;

    public double UsedRatio => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0d;
}
