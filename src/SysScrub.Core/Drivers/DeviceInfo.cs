using SysScrub.Core.Formatting;

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

    /// <summary>Aygıt Yöneticisi sorun kodunun okunabilir karşılığı.</summary>
    public string ProblemDescription => ProblemCode switch
    {
        0 => string.Empty,
        1 => CoreText.Get("Dv_P1", "Sürücü yapılandırılmamış."),
        3 => CoreText.Get("Dv_P3", "Sürücü bozuk ya da bellek yetersiz."),
        10 => CoreText.Get("Dv_P10", "Cihaz başlatılamıyor."),
        12 => CoreText.Get("Dv_P12", "Yeterli boş kaynak bulunamadı."),
        14 => CoreText.Get("Dv_P14", "Bilgisayarın yeniden başlatılması gerekiyor."),
        16 => CoreText.Get("Dv_P16", "Cihazın kullandığı kaynaklar tam olarak belirlenemedi."),
        18 => CoreText.Get("Dv_P18", "Sürücülerin yeniden yüklenmesi gerekiyor."),
        19 => CoreText.Get("Dv_P19", "Kayıt defteri bilgisi bozuk ya da eksik."),
        21 => CoreText.Get("Dv_P21", "Windows cihazı kaldırıyor."),
        22 => CoreText.Get("Dv_P22", "Cihaz devre dışı bırakılmış."),
        24 => CoreText.Get("Dv_P24", "Cihaz takılı değil ya da düzgün çalışmıyor."),
        28 => CoreText.Get("Dv_P28", "Sürücü yüklü değil."),
        31 => CoreText.Get("Dv_P31", "Windows bu cihaz için gereken sürücüleri yükleyemiyor."),
        32 => CoreText.Get("Dv_P32", "Cihazın başlatma servisi devre dışı."),
        37 => CoreText.Get("Dv_P37", "Sürücü başlatılamadı."),
        39 => CoreText.Get("Dv_P39", "Sürücü bozuk ya da eksik."),
        43 => CoreText.Get("Dv_P43", "Cihaz bir sorun bildirdiği için Windows onu durdurdu."),
        45 => CoreText.Get("Dv_P45", "Cihaz şu an bilgisayara bağlı değil."),
        _ => CoreText.Format("Dv_PUnknown", "Aygıt Yöneticisi sorun kodu {0}.", ProblemCode)
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

/// <summary>Windows cihaz sınıfı adlarının okunabilir karşılıkları.</summary>
public static class DeviceClassNames
{
    /// <summary>
    /// Windows cihaz sınıfı -> (katalog anahtarı, Türkçe karşılık).
    /// Anahtar sabit, sözcük dile göre değişiyor.
    /// </summary>
    private static readonly Dictionary<string, (string Key, string Turkish)> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Display"] = ("Dv_C_Display", "Ekran kartları"),
        ["Net"] = ("Dv_C_Net", "Ağ bağdaştırıcıları"),
        ["Media"] = ("Dv_C_Media", "Ses, video ve oyun denetleyicileri"),
        ["AudioEndpoint"] = ("Dv_C_AudioEndpoint", "Ses girişleri ve çıkışları"),
        ["USB"] = ("Dv_C_USB", "USB denetleyicileri"),
        ["DiskDrive"] = ("Dv_C_DiskDrive", "Disk sürücüleri"),
        ["SCSIAdapter"] = ("Dv_C_SCSIAdapter", "Depolama denetleyicileri"),
        ["HIDClass"] = ("Dv_C_HIDClass", "İnsan arabirim aygıtları"),
        ["Keyboard"] = ("Dv_C_Keyboard", "Klavyeler"),
        ["Mouse"] = ("Dv_C_Mouse", "Fare ve işaret aygıtları"),
        ["Monitor"] = ("Dv_C_Monitor", "Monitörler"),
        ["Printer"] = ("Dv_C_Printer", "Yazıcılar"),
        ["Bluetooth"] = ("Dv_C_Bluetooth", "Bluetooth"),
        ["Camera"] = ("Dv_C_Camera", "Kameralar"),
        ["Image"] = ("Dv_C_Image", "Görüntü aygıtları"),
        ["Battery"] = ("Dv_C_Battery", "Piller"),
        ["Processor"] = ("Dv_C_Processor", "İşlemciler"),
        ["System"] = ("Dv_C_System", "Sistem aygıtları"),
        ["Computer"] = ("Dv_C_Computer", "Bilgisayar"),
        ["Firmware"] = ("Dv_C_Firmware", "Bellenim"),
        ["Ports"] = ("Dv_C_Ports", "Bağlantı noktaları"),
        ["SoftwareComponent"] = ("Dv_C_SoftwareComponent", "Yazılım bileşenleri"),
        ["SoftwareDevice"] = ("Dv_C_SoftwareDevice", "Yazılım aygıtları"),
        ["Volume"] = ("Dv_C_Volume", "Birimler"),
        ["SecurityDevices"] = ("Dv_C_SecurityDevices", "Güvenlik aygıtları"),
        ["SmartCardReader"] = ("Dv_C_SmartCardReader", "Akıllı kart okuyucuları"),
        ["Sensor"] = ("Dv_C_Sensor", "Algılayıcılar"),
        ["Net_Virtual"] = ("Dv_C_NetVirtual", "Sanal ağ bağdaştırıcıları"),
        ["MEDIA"] = ("Dv_C_MEDIA", "Ses, video ve oyun denetleyicileri"),
        ["AudioProcessingObject"] = ("Dv_C_AudioProcessingObject", "Ses işleme bileşenleri"),
        ["MediaStreamingDevices"] = ("Dv_C_MediaStreamingDevices", "Medya akış aygıtları"),
        ["PrintQueue"] = ("Dv_C_PrintQueue", "Yazdırma kuyrukları"),
        ["Display_Virtual"] = ("Dv_C_DisplayVirtual", "Sanal ekranlar"),
        ["Extension"] = ("Dv_C_Extension", "Aygıt uzantıları"),
        ["USBDevice"] = ("Dv_C_USBDevice", "USB aygıtları"),
        ["WSDPrintDevice"] = ("Dv_C_WSDPrintDevice", "Ağ yazıcıları"),
        ["Biometric"] = ("Dv_C_Biometric", "Biyometrik aygıtlar"),
        ["DigitalMediaDevices"] = ("Dv_C_DigitalMediaDevices", "Dijital medya aygıtları"),
        ["Modem"] = ("Dv_C_Modem", "Modemler"),
        ["CDROM"] = ("Dv_C_CDROM", "DVD/CD-ROM sürücüleri"),
        ["FloppyDisk"] = ("Dv_C_FloppyDisk", "Disket sürücüleri"),
        ["hdc"] = ("Dv_C_hdc", "IDE ATA/ATAPI denetleyicileri"),
        ["MTD"] = ("Dv_C_MTD", "Bellek teknolojisi aygıtları"),
        ["Net Service"] = ("Dv_C_NetService", "Ağ hizmetleri"),
        ["NetTrans"] = ("Dv_C_NetTrans", "Ağ protokolleri"),
        ["NetClient"] = ("Dv_C_NetClient", "Ağ istemcileri"),
        ["Other"] = ("Dv_C_Other", "Diğer aygıtlar"),
    };

    public static string Describe(string? className) =>
        className is not null && Names.TryGetValue(className, out (string Key, string Turkish) name)
            ? CoreText.Get(name.Key, name.Turkish)
            : className ?? CoreText.Get("Dv_C_Other", "Diğer aygıtlar");
}
