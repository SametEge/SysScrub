namespace SysScrub.Core.Drivers;

/// <summary>Bir cihazın sürücüsünün güncellik durumu.</summary>
public enum DriverStatus
{
    /// <summary>Bilinen bir kaynakta daha yenisi yok.</summary>
    UpToDate,

    /// <summary>Windows Update daha yeni bir sürücü sunuyor. Kesin bilgi.</summary>
    UpdateAvailable,

    /// <summary>
    /// Sürücü eski ama hiçbir kaynak yenisini sunmuyor. Üreticinin sitesinde
    /// daha yenisi olabilir; "kesin eski" demiyoruz çünkü bilmiyoruz.
    /// </summary>
    PossiblyOutdated
}

/// <summary>Bir cihaz ve sürücüsünün durumu; listede tek satır.</summary>
public sealed record DriverStatusRow
{
    public required DeviceInfo Device { get; init; }

    /// <summary>Windows Update'in sunduğu güncelleme. Yoksa null.</summary>
    public DriverUpdate? Update { get; init; }

    public required DriverStatus Status { get; init; }

    public bool NeedsAttention => Status != DriverStatus.UpToDate;

    /// <summary>Kurulu sürücünün sürümü ve tarihi.</summary>
    public string InstalledLabel
    {
        get
        {
            string version = Device.DriverVersion ?? "sürüm yok";
            string date = Device.DriverDate?.ToString("dd.MM.yyyy") ?? "tarih yok";

            return $"{version}  ·  {date}";
        }
    }

    /// <summary>Sunulan sürücünün sürümü ve tarihi; yoksa neden bilinmediği.</summary>
    public string AvailableLabel
    {
        get
        {
            if (Update is null)
            {
                return Status == DriverStatus.PossiblyOutdated
                    ? "üreticide olabilir"
                    : "güncel";
            }

            string version = Update.Version ?? "yeni sürüm";
            string date = Update.Date?.ToString("dd.MM.yyyy") ?? "tarih yok";

            return $"{version}  ·  {date}";
        }
    }

    /// <summary>Sürücünün kaç yıllık olduğu; iki yıldan yeniyse boş.</summary>
    public string AgeLabel
    {
        get
        {
            if (Device.DriverAge is not { } age)
            {
                return string.Empty;
            }

            int years = (int)(age.TotalDays / 365);

            return years >= 2 ? $"{years} yıl eski" : string.Empty;
        }
    }
}

/// <summary>Envanter ile güncelleme kaynağının birleştirilmiş sonucu.</summary>
public sealed record DriverStatusReport
{
    public required IReadOnlyList<DriverStatusRow> Rows { get; init; }

    /// <summary>Windows Update sorgulandı mı. Sorgulanmadıysa "güncel" iddiası zayıftır.</summary>
    public required bool UpdateSourceQueried { get; init; }

    public string? UpdateSourceMessage { get; init; }

    public static DriverStatusReport Empty { get; } = new() { Rows = [], UpdateSourceQueried = false };

    public IReadOnlyList<DriverStatusRow> Outdated =>
        Rows.Where(r => r.Status == DriverStatus.UpdateAvailable).ToArray();

    public IReadOnlyList<DriverStatusRow> PossiblyOutdated =>
        Rows.Where(r => r.Status == DriverStatus.PossiblyOutdated).ToArray();

    public IReadOnlyList<DriverStatusRow> UpToDate =>
        Rows.Where(r => r.Status == DriverStatus.UpToDate).ToArray();

    public int AttentionCount => Rows.Count(r => r.NeedsAttention);
}

/// <summary>
/// Donanım envanteriyle Windows Update sonucunu eşleştirir.
///
/// Eşleşme donanım kimliği üzerinden yapılır: WUA'nın verdiği DriverHardwareID,
/// cihazın kimlik listesinde geçiyorsa o güncelleme o cihaza aittir. Ada göre
/// eşleştirmek yanlış cihaza güncelleme bağlamaya yol açardı.
/// </summary>
public static class DriverStatusMatcher
{
    /// <summary>Bu yaştan eski üretici sürücüleri "muhtemelen eski" sayılır.</summary>
    private const int PossiblyOutdatedYears = 2;

    public static DriverStatusReport Build(
        DeviceInventoryReport inventory,
        DriverSearchResult? search)
    {
        Dictionary<string, DriverUpdate> byHardwareId = IndexUpdates(search);
        var rows = new List<DriverStatusRow>(inventory.Devices.Count);

        foreach (DeviceInfo device in inventory.Devices)
        {
            DriverUpdate? update = FindUpdate(device, byHardwareId);
            DriverStatus status = Classify(device, update);

            rows.Add(new DriverStatusRow { Device = device, Update = update, Status = status });
        }

        return new DriverStatusReport
        {
            // Güncel değil olanlar üstte, sonra muhtemelen eskiler, ikisi de yaşa göre
            Rows = rows
                .OrderBy(r => r.Status switch
                {
                    DriverStatus.UpdateAvailable => 0,
                    DriverStatus.PossiblyOutdated => 1,
                    _ => 2
                })
                .ThenByDescending(r => r.Device.DriverAge ?? TimeSpan.Zero)
                .ThenBy(r => r.Device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            UpdateSourceQueried = search is not null,
            UpdateSourceMessage = search?.Describe()
        };
    }

    private static Dictionary<string, DriverUpdate> IndexUpdates(DriverSearchResult? search)
    {
        var index = new Dictionary<string, DriverUpdate>(StringComparer.OrdinalIgnoreCase);

        if (search is null)
        {
            return index;
        }

        foreach (DriverUpdate update in search.Updates)
        {
            if (!string.IsNullOrWhiteSpace(update.HardwareId))
            {
                index[update.HardwareId] = update;
            }
        }

        return index;
    }

    private static DriverUpdate? FindUpdate(DeviceInfo device, Dictionary<string, DriverUpdate> byHardwareId)
    {
        if (byHardwareId.Count == 0)
        {
            return null;
        }

        foreach (string hardwareId in device.HardwareIds)
        {
            if (byHardwareId.TryGetValue(hardwareId, out DriverUpdate? update))
            {
                return update;
            }
        }

        return null;
    }

    private static DriverStatus Classify(DeviceInfo device, DriverUpdate? update)
    {
        if (update is not null)
        {
            return DriverStatus.UpdateAvailable;
        }

        // Microsoft'un genel sürücüleri eskimez: Windows'la birlikte gelir ve
        // sürüm numaraları işletim sistemine bağlıdır. Bunları "eski" saymak yanıltıcı.
        if (device.UsesGenericDriver)
        {
            return DriverStatus.UpToDate;
        }

        // Sürücü tarihi bilinmiyorsa bir iddiada bulunmuyoruz.
        if (device.DriverAge is not { } age)
        {
            return DriverStatus.UpToDate;
        }

        return age.TotalDays > PossiblyOutdatedYears * 365
            ? DriverStatus.PossiblyOutdated
            : DriverStatus.UpToDate;
    }
}
