using Microsoft.Win32;
using SysScrub.Core.Rules;

namespace SysScrub.Core.RegistryCleaning.Scanners;

/// <summary>
/// Ortak temel: bir anahtarın altındaki değerleri gezip her birinin işaret ettiği
/// dosyanın var olup olmadığına bakan tarayıcılar.
/// </summary>
public abstract class PathValueScannerBase : IRegistryScanner
{
    public abstract string Id { get; }

    public abstract string Title { get; }

    public abstract string Explanation { get; }

    public virtual RiskLevel Risk => RiskLevel.Safe;

    public virtual bool DefaultEnabled => true;

    public virtual bool RequiresAdmin => false;

    public abstract IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken);

    /// <summary>
    /// Bir değerin işaret ettiği dosyayı denetler ve ölüyse bulgu üretir.
    /// Çözülemeyen yollar için bulgu üretilmez — emin olmadığımız kaydı silmiyoruz.
    /// </summary>
    protected RegistryFinding? ProbeValue(
        RegistryLocation location,
        string? rawValue,
        string reason = "İşaret ettiği dosya yok")
    {
        if (RegistryPathProbe.Probe(rawValue, out string resolved) != RegistryPathProbe.ProbeResult.Missing)
        {
            return null;
        }

        return new RegistryFinding
        {
            ScannerId = Id,
            Location = location,
            Reason = reason,
            Target = resolved,
            Risk = Risk
        };
    }
}

/// <summary>Kurulumların bıraktığı paylaşılan DLL sayaçları; dosyası gitmiş olanlar ölüdür.</summary>
public sealed class SharedDllScanner : PathValueScannerBase
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";

    public override string Id => "shared-dlls";

    public override string Title => "Eksik paylaşılan DLL kayıtları";

    public override string Explanation =>
        "Programlar, ortak kullandıkları kütüphaneleri burada sayar. Program kaldırıldığında " +
        "sayaç bazen geride kalır. Dosyası artık var olmayan kayıtlar hiçbir işe yaramaz.";

    public override bool RequiresAdmin => true;

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        foreach (RegistryView view in RegistryReader.BothViews)
        {
            using RegistryKey? key = RegistryReader.OpenKey(RegistryHive.LocalMachine, view, KeyPath);

            foreach (string valueName in RegistryReader.ValueNames(key))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Burada değerin ADI dosya yoludur, verisi referans sayısıdır.
                RegistryFinding? finding = ProbeValue(
                    new RegistryLocation
                    {
                        Hive = RegistryHive.LocalMachine,
                        View = view,
                        KeyPath = KeyPath,
                        ValueName = valueName
                    },
                    valueName);

                if (finding is not null)
                {
                    yield return finding;
                }
            }
        }
    }
}

/// <summary>Uygulama yolları: Çalıştır kutusundan "app.exe" yazılınca aranan kayıtlar.</summary>
public sealed class AppPathScanner : PathValueScannerBase
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    public override string Id => "app-paths";

    public override string Title => "Geçersiz uygulama yolları";

    public override string Explanation =>
        "Çalıştır kutusuna kısa ad yazınca (örneğin \"chrome\") Windows programı burada arar. " +
        "Program kaldırıldığında kayıt bazen kalır ve kısa ad çalışmayan bir yola işaret eder.";

    public override bool RequiresAdmin => true;

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        foreach (RegistryView view in RegistryReader.BothViews)
        {
            using RegistryKey? root = RegistryReader.OpenKey(RegistryHive.LocalMachine, view, KeyPath);

            foreach (string subKeyName in RegistryReader.SubKeyNames(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using RegistryKey? entry = RegistryReader.OpenSubKey(root, subKeyName);
                string? target = RegistryReader.StringValue(entry);

                RegistryFinding? finding = ProbeValue(
                    new RegistryLocation
                    {
                        Hive = RegistryHive.LocalMachine,
                        View = view,
                        KeyPath = $@"{KeyPath}\{subKeyName}"
                    },
                    target);

                if (finding is not null)
                {
                    yield return finding;
                }
            }
        }
    }
}

/// <summary>Başlangıçta çalıştırılmak istenen ama artık var olmayan programlar.</summary>
public sealed class StartupEntryScanner : PathValueScannerBase
{
    private static readonly (RegistryHive Hive, string Path)[] Locations =
    [
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce")
    ];

    public override string Id => "startup-entries";

    public override string Title => "Hedefi olmayan başlangıç kayıtları";

    public override string Explanation =>
        "Windows açılışta bu kayıtlardaki programları çalıştırmayı dener. Program silinmişse " +
        "her açılışta boşuna aranır. Bunları temizlemek açılışı biraz hızlandırır.";

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        foreach ((RegistryHive hive, string keyPath) in Locations)
        {
            foreach (RegistryView view in RegistryReader.ViewsFor(hive, keyPath))
            {
                using RegistryKey? key = RegistryReader.OpenKey(hive, view, keyPath);

                foreach (string valueName in RegistryReader.ValueNames(key))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    RegistryFinding? finding = ProbeValue(
                        new RegistryLocation
                        {
                            Hive = hive,
                            View = view,
                            KeyPath = keyPath,
                            ValueName = valueName
                        },
                        RegistryReader.StringValue(key, valueName));

                    if (finding is not null)
                    {
                        yield return finding;
                    }
                }
            }
        }
    }
}

/// <summary>Son çalıştırılan programların görünen adlarını tutan önbellek.</summary>
public sealed class MuiCacheScanner : PathValueScannerBase
{
    private const string KeyPath =
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";

    public override string Id => "muicache";

    public override string Title => "MUICache ölü kayıtları";

    public override string Explanation =>
        "Windows, çalıştırdığın programların görünen adlarını burada saklar. Silinen programların " +
        "kayıtları kalır ve yıllar içinde birikir. Temizlenmesi hiçbir şeyi etkilemez.";

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        using RegistryKey? key = RegistryReader.OpenKey(
            RegistryHive.CurrentUser, RegistryView.Registry64, KeyPath);

        foreach (string valueName in RegistryReader.ValueNames(key))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Değer adı ".FriendlyAppName" gibi son eklerle bitebiliyor; yol kısmı öndeki parça.
            string path = valueName;
            int suffix = path.IndexOf(".FriendlyAppName", StringComparison.OrdinalIgnoreCase);

            if (suffix > 0)
            {
                path = path[..suffix];
            }
            else if (path.EndsWith(".ApplicationCompany", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^".ApplicationCompany".Length];
            }

            RegistryFinding? finding = ProbeValue(
                new RegistryLocation
                {
                    Hive = RegistryHive.CurrentUser,
                    KeyPath = KeyPath,
                    ValueName = valueName
                },
                path);

            if (finding is not null)
            {
                yield return finding;
            }
        }
    }
}

/// <summary>Yükleyicinin kaydettiği ama artık var olmayan klasörler.</summary>
public sealed class InstallerFolderScanner : PathValueScannerBase
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders";

    public override string Id => "installer-folders";

    public override string Title => "Kırık yükleyici klasör kayıtları";

    public override string Explanation =>
        "Windows Installer, kurulum yaptığı klasörleri burada listeler. Klasör silindiğinde " +
        "kayıt kalır ve zamanla birikir.";

    public override bool RequiresAdmin => true;

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        using RegistryKey? key = RegistryReader.OpenKey(
            RegistryHive.LocalMachine, RegistryView.Registry64, KeyPath);

        foreach (string valueName in RegistryReader.ValueNames(key))
        {
            cancellationToken.ThrowIfCancellationRequested();

            RegistryFinding? finding = ProbeValue(
                new RegistryLocation
                {
                    Hive = RegistryHive.LocalMachine,
                    KeyPath = KeyPath,
                    ValueName = valueName
                },
                valueName,
                "İşaret ettiği klasör yok");

            if (finding is not null)
            {
                yield return finding;
            }
        }
    }
}

/// <summary>Ses şemalarında artık var olmayan .wav dosyalarına işaret eden olaylar.</summary>
public sealed class SoundEventScanner : PathValueScannerBase
{
    private const string KeyPath = @"AppEvents\Schemes\Apps";

    public override string Id => "sound-events";

    public override string Title => "Sahipsiz ses olayları";

    public override string Explanation =>
        "Sistem olaylarına atanmış ses dosyaları. Dosya silindiğinde olay sessiz kalır " +
        "ama kayıt durur. Temizlemek yalnızca ölü kaydı kaldırır, sesli olayları etkilemez.";

    public override bool DefaultEnabled => false;

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        using RegistryKey? apps = RegistryReader.OpenKey(
            RegistryHive.CurrentUser, RegistryView.Registry64, KeyPath);

        foreach (string appName in RegistryReader.SubKeyNames(apps))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using RegistryKey? app = RegistryReader.OpenSubKey(apps, appName);

            foreach (string eventName in RegistryReader.SubKeyNames(app))
            {
                using RegistryKey? soundEvent = RegistryReader.OpenSubKey(app, eventName);
                using RegistryKey? current = RegistryReader.OpenSubKey(soundEvent, ".Current");

                string? wav = RegistryReader.StringValue(current);

                // Boş değer "ses yok" demektir, ölü kayıt değil.
                if (string.IsNullOrWhiteSpace(wav))
                {
                    continue;
                }

                RegistryFinding? finding = ProbeValue(
                    new RegistryLocation
                    {
                        Hive = RegistryHive.CurrentUser,
                        KeyPath = $@"{KeyPath}\{appName}\{eventName}\.Current",
                        ValueName = string.Empty
                    },
                    wav,
                    "İşaret ettiği ses dosyası yok");

                if (finding is not null)
                {
                    yield return finding;
                }
            }
        }
    }
}
