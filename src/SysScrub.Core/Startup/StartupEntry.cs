namespace SysScrub.Core.Startup;

/// <summary>Bir başlangıç öğesinin nereden geldiği.</summary>
public enum StartupSource
{
    /// <summary>Run kaydı — en yaygın başlangıç yöntemi.</summary>
    RegistryRun,

    /// <summary>RunOnce — bir kez çalışıp kendini silmesi beklenir.</summary>
    RegistryRunOnce,

    /// <summary>Başlangıç klasöründeki kısayol.</summary>
    StartupFolder,

    /// <summary>Oturum açma tetikleyicili zamanlanmış görev.</summary>
    ScheduledTask,

    /// <summary>Otomatik başlayan servis.</summary>
    Service
}

/// <summary>Öğenin değiştirilip değiştirilemeyeceği.</summary>
public enum StartupControl
{
    /// <summary>Açılıp kapatılabilir.</summary>
    Toggleable,

    /// <summary>
    /// Yalnızca gösterilir. Servisler bu gruba giriyor: başlangıç türünü değiştirmek
    /// bambaşka bir risk sınıfı ve sistem kararlılığını doğrudan etkileyebiliyor.
    /// </summary>
    ReadOnly
}

/// <summary>Açılışta çalışan tek bir öğe.</summary>
public sealed record StartupEntry
{
    /// <summary>Açma/kapama işlemlerinde kullanılan kararlı kimlik.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Çalıştırılan komut satırı.</summary>
    public required string Command { get; init; }

    public required StartupSource Source { get; init; }

    public required bool IsEnabled { get; init; }

    public StartupControl Control { get; init; } = StartupControl.Toggleable;

    /// <summary>HKLM/ortak klasör kaynaklı mı; bu öğeler tüm kullanıcıları etkiler.</summary>
    public bool IsMachineWide { get; init; }

    /// <summary>Komutun işaret ettiği çözümlenmiş dosya yolu.</summary>
    public string? TargetPath { get; init; }

    /// <summary>Hedef dosya bulunamadı — açılışta boşuna aranıyor demektir.</summary>
    public bool TargetMissing { get; init; }

    public string? Publisher { get; init; }

    /// <summary>
    /// Windows'un ölçtüğü açılış gecikmesi (milisaniye). Tahmin değil:
    /// Tanılama-Performans olay günlüğünden okunur. Ölçüm yoksa null.
    /// </summary>
    public int? BootDelayMs { get; init; }

    /// <summary>Registry kaynaklı öğelerde anahtar yolu; açma/kapama bunu kullanır.</summary>
    public string? RegistryKeyPath { get; init; }

    public bool IsMachineHive { get; init; }

    /// <summary>
    /// Windows'un onay anahtarındaki karşılığı. Açma/kapama işlemi yolu yeniden
    /// çözmek yerine bunu kullanır — envanterde ne bulduysak onu yazıyoruz.
    /// </summary>
    public StartupApprovedStore.ApprovalScope? ApprovalScope { get; init; }

    /// <summary>
    /// Onay anahtarındaki değer adı. Registry öğelerinde değer adı, klasör
    /// öğelerinde uzantısıyla birlikte dosya adı (<see cref="Name"/> uzantısız).
    /// </summary>
    public string? ApprovalValueName { get; init; }

    /// <summary>Başlangıç klasörü öğelerinde kısayol dosyası.</summary>
    public string? ShortcutPath { get; init; }

    /// <summary>Zamanlanmış görev öğelerinde tam görev yolu.</summary>
    public string? TaskPath { get; init; }

    public string SourceLabel => Source switch
    {
        StartupSource.RegistryRun => IsMachineWide ? "Kayıt defteri (tüm kullanıcılar)" : "Kayıt defteri",
        StartupSource.RegistryRunOnce => "Kayıt defteri (bir kerelik)",
        StartupSource.StartupFolder => IsMachineWide ? "Başlangıç klasörü (ortak)" : "Başlangıç klasörü",
        StartupSource.ScheduledTask => "Zamanlanmış görev",
        StartupSource.Service => "Servis",
        _ => "Bilinmiyor"
    };

    /// <summary>Açılışa etkisinin sözel karşılığı.</summary>
    public string ImpactLabel => BootDelayMs switch
    {
        null => "ölçülmedi",
        < 300 => "düşük",
        < 1000 => "orta",
        _ => "yüksek"
    };

    public override string ToString() => $"{Name} ({SourceLabel})";
}

/// <summary>Kaynağa göre gruplanmış envanter.</summary>
public sealed record StartupInventoryReport
{
    public required IReadOnlyList<StartupEntry> Entries { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary>Açılış ölçümü okunabildi mi. Okunamadıysa etki sütunu boş kalır.</summary>
    public bool BootMeasurementsAvailable { get; init; }

    public static StartupInventoryReport Empty { get; } = new() { Entries = [], Duration = TimeSpan.Zero };

    public int EnabledCount => Entries.Count(e => e.IsEnabled);

    public int DisabledCount => Entries.Count(e => !e.IsEnabled);

    /// <summary>Hedefi kaybolmuş öğeler — açılışta boşuna aranıyorlar.</summary>
    public IReadOnlyList<StartupEntry> BrokenEntries =>
        Entries.Where(e => e.TargetMissing).ToArray();

    /// <summary>Ölçülen toplam açılış gecikmesi.</summary>
    public int TotalDelayMs => Entries.Where(e => e.IsEnabled).Sum(e => e.BootDelayMs ?? 0);
}
