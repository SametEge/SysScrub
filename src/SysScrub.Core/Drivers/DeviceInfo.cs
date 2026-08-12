namespace SysScrub.Core.Drivers;

/// <summary>Bir cihazın çalışma durumu.</summary>
public enum DeviceHealth
{
    /// <summary>Sorunsuz çalışıyor.</summary>
    Working,

    /// <summary>Sürücüsü yok veya yüklenemiyor — Aygıt Yöneticisi'ndeki sarı ünlem.</summary>
    Problem,

    /// <summary>Kullanıcı tarafından devre dışı bırakılmış.</summary>
    Disabled,

    /// <summary>Sistemde kayıtlı ama şu an takılı değil.</summary>
    NotPresent
}

/// <summary>Tek bir donanım cihazı ve sürücüsü.</summary>
public sealed record DeviceInfo
{
    /// <summary>PnP örnek kimliği: PCI\VEN_10DE&amp;DEV_2504&amp;SUBSYS_...\4&amp;1a2b3c4d&amp;0&amp;0008</summary>
    public required string DeviceId { get; init; }

    public required string Name { get; init; }

    /// <summary>Aygıt Yöneticisi'ndeki kategori: Display, Net, Media, USB…</summary>
    public string? DeviceClass { get; init; }

    public string? Manufacturer { get; init; }

    public string? DriverVersion { get; init; }

    public DateTime? DriverDate { get; init; }

    /// <summary>Sürücüyü yazan firma. Üreticiden farklı olabilir (Microsoft genel sürücüleri).</summary>
    public string? DriverProvider { get; init; }

    /// <summary>Sürücü paketinin INF dosyası — yedekleme ve geri alma bununla eşleşir.</summary>
    public string? InfName { get; init; }

    public bool IsSigned { get; init; }

    /// <summary>
    /// Donanım kimlikleri, en özelden en genele sıralı.
    /// Sürücü eşleştirmesi bu sırayı kullanır: tam eşleşme, uyumlu eşleşmeyi yener.
    /// </summary>
    public IReadOnlyList<string> HardwareIds { get; init; } = [];

    public int ProblemCode { get; init; }

    public DeviceHealth Health { get; init; } = DeviceHealth.Working;

    public bool HasProblem => Health == DeviceHealth.Problem;

    /// <summary>Sürücünün yaşı. Tarihi bilinmiyorsa null.</summary>
    public TimeSpan? DriverAge => DriverDate is { } date ? DateTime.Now - date : null;

    /// <summary>
    /// Microsoft'un genel sürücüsüyle mi çalışıyor. Genel sürücü çalışır ama
    /// üreticinin sürücüsündeki özellikleri ve başarımı vermez.
    /// </summary>
    public bool UsesGenericDriver =>
        DriverProvider is not null &&
        DriverProvider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);

    /// <summary>Aygıt Yöneticisi sorun kodunun Türkçe karşılığı.</summary>
    public string ProblemDescription => ProblemCode switch
    {
        0 => string.Empty,
        1 => "Sürücü yapılandırılmamış.",
        3 => "Sürücü bozuk ya da bellek yetersiz.",
        10 => "Cihaz başlatılamıyor.",
        12 => "Yeterli boş kaynak bulunamadı.",
        14 => "Bilgisayarın yeniden başlatılması gerekiyor.",
        16 => "Cihazın kullandığı kaynaklar tam olarak belirlenemedi.",
        18 => "Sürücülerin yeniden yüklenmesi gerekiyor.",
        19 => "Kayıt defteri bilgisi bozuk ya da eksik.",
        21 => "Windows cihazı kaldırıyor.",
        22 => "Cihaz devre dışı bırakılmış.",
        24 => "Cihaz takılı değil ya da düzgün çalışmıyor.",
        28 => "Sürücü yüklü değil.",
        31 => "Windows bu cihaz için gereken sürücüleri yükleyemiyor.",
        32 => "Cihazın başlatma servisi devre dışı.",
        37 => "Sürücü başlatılamadı.",
        39 => "Sürücü bozuk ya da eksik.",
        43 => "Cihaz bir sorun bildirdiği için Windows onu durdurdu.",
        45 => "Cihaz şu an bilgisayara bağlı değil.",
        _ => $"Aygıt Yöneticisi sorun kodu {ProblemCode}."
    };

    public override string ToString() => $"{Name} ({DriverVersion})";
}

/// <summary>Cihaz sınıfına göre gruplanmış envanter.</summary>
public sealed record DeviceGroup(string ClassName, string DisplayName, IReadOnlyList<DeviceInfo> Devices)
{
    public int ProblemCount => Devices.Count(d => d.HasProblem);
}

/// <summary>Tam donanım envanteri.</summary>
public sealed record DeviceInventoryReport
{
    public required IReadOnlyList<DeviceInfo> Devices { get; init; }

    public required TimeSpan Duration { get; init; }

    public static DeviceInventoryReport Empty { get; } = new() { Devices = [], Duration = TimeSpan.Zero };

    public IReadOnlyList<DeviceInfo> ProblemDevices =>
        Devices.Where(d => d.HasProblem).ToArray();

    /// <summary>İki yıldan eski üçüncü parti sürücüler; güncelleme adayları.</summary>
    public IReadOnlyList<DeviceInfo> AgingDrivers =>
        Devices
            .Where(d => !d.UsesGenericDriver && d.DriverAge is { TotalDays: > 730 })
            .OrderByDescending(d => d.DriverAge)
            .ToArray();

    public IReadOnlyList<DeviceGroup> GroupByClass() =>
        Devices
            .GroupBy(d => d.DeviceClass ?? "Other", StringComparer.OrdinalIgnoreCase)
            .Select(g => new DeviceGroup(g.Key, DeviceClassNames.Describe(g.Key), g.OrderBy(d => d.Name).ToArray()))
            // Sorunlu cihazı olan sınıflar üstte: kullanıcının önce görmesi gereken onlar.
            .OrderByDescending(g => g.ProblemCount)
            .ThenBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
}

/// <summary>Windows cihaz sınıfı adlarının Türkçe karşılıkları.</summary>
public static class DeviceClassNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Display"] = "Ekran kartları",
        ["Net"] = "Ağ bağdaştırıcıları",
        ["Media"] = "Ses, video ve oyun denetleyicileri",
        ["AudioEndpoint"] = "Ses girişleri ve çıkışları",
        ["USB"] = "USB denetleyicileri",
        ["DiskDrive"] = "Disk sürücüleri",
        ["SCSIAdapter"] = "Depolama denetleyicileri",
        ["HIDClass"] = "İnsan arabirim aygıtları",
        ["Keyboard"] = "Klavyeler",
        ["Mouse"] = "Fare ve işaret aygıtları",
        ["Monitor"] = "Monitörler",
        ["Printer"] = "Yazıcılar",
        ["Bluetooth"] = "Bluetooth",
        ["Camera"] = "Kameralar",
        ["Image"] = "Görüntü aygıtları",
        ["Battery"] = "Piller",
        ["Processor"] = "İşlemciler",
        ["System"] = "Sistem aygıtları",
        ["Computer"] = "Bilgisayar",
        ["Firmware"] = "Bellenim",
        ["Ports"] = "Bağlantı noktaları",
        ["SoftwareComponent"] = "Yazılım bileşenleri",
        ["SoftwareDevice"] = "Yazılım aygıtları",
        ["Volume"] = "Birimler",
        ["SecurityDevices"] = "Güvenlik aygıtları",
        ["SmartCardReader"] = "Akıllı kart okuyucuları",
        ["Sensor"] = "Algılayıcılar",
        ["Net_Virtual"] = "Sanal ağ bağdaştırıcıları",
        ["MEDIA"] = "Ses, video ve oyun denetleyicileri",
        ["AudioProcessingObject"] = "Ses işleme bileşenleri",
        ["MediaStreamingDevices"] = "Medya akış aygıtları",
        ["PrintQueue"] = "Yazdırma kuyrukları",
        ["Display_Virtual"] = "Sanal ekranlar",
        ["Extension"] = "Aygıt uzantıları",
        ["USBDevice"] = "USB aygıtları",
        ["WSDPrintDevice"] = "Ağ yazıcıları",
        ["Biometric"] = "Biyometrik aygıtlar",
        ["DigitalMediaDevices"] = "Dijital medya aygıtları",
        ["Modem"] = "Modemler",
        ["CDROM"] = "DVD/CD-ROM sürücüleri",
        ["FloppyDisk"] = "Disket sürücüleri",
        ["hdc"] = "IDE ATA/ATAPI denetleyicileri",
        ["MTD"] = "Bellek teknolojisi aygıtları",
        ["Net Service"] = "Ağ hizmetleri",
        ["NetTrans"] = "Ağ protokolleri",
        ["NetClient"] = "Ağ istemcileri",
        ["Other"] = "Diğer aygıtlar"
    };

    public static string Describe(string? className) =>
        className is not null && Names.TryGetValue(className, out string? name) ? name : className ?? "Diğer aygıtlar";
}
