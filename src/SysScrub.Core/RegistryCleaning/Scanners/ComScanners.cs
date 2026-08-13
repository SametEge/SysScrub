using Microsoft.Win32;
using SysScrub.Core.Formatting;
using SysScrub.Core.Rules;

namespace SysScrub.Core.RegistryCleaning.Scanners;

/// <summary>Dosya uzantısı kayıtları: hangi uzantının hangi program türüne ait olduğu.</summary>
public sealed class FileExtensionScanner : PathValueScannerBase
{
    public override string Id => "file-extensions";

    public override string Title =>
        CoreText.Get("Rs_FileExtensions_Title", "Sahipsiz dosya uzantıları");

    public override string Explanation =>
        CoreText.Get("Rs_FileExtensions_Desc",
        "Her dosya uzantısı bir program türüne (ProgID) bağlıdır. Program kaldırıldığında " +
        "uzantı kaydı bazen kalır ve artık var olmayan bir türe işaret eder. Bu, dosyaya " +
        "çift tıklandığında Windows'un ne yapacağını bilememesine yol açar.");


    public override RiskLevel Risk => RiskLevel.Caution;

    public override bool DefaultEnabled => false;

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        foreach (RegistryHive hive in (RegistryHive[])[RegistryHive.LocalMachine, RegistryHive.CurrentUser])
        {
            using RegistryKey? classes = RegistryReader.OpenKey(hive, RegistryView.Registry64, @"SOFTWARE\Classes");

            foreach (string name in RegistryReader.SubKeyNames(classes))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!name.StartsWith('.'))
                {
                    continue;
                }

                using RegistryKey? extension = RegistryReader.OpenSubKey(classes, name);
                string? progId = RegistryReader.StringValue(extension);

                // Varsayılan değeri boş olan uzantılar geçerlidir: "Birlikte aç" listesi
                // OpenWithProgids alt anahtarında durur, ProgID'siz kayıt ölü değildir.
                if (string.IsNullOrWhiteSpace(progId))
                {
                    continue;
                }

                if (ProgIdExists(progId))
                {
                    continue;
                }

                yield return new RegistryFinding
                {
                    ScannerId = Id,
                    Location = new RegistryLocation
                    {
                        Hive = hive,
                        KeyPath = $@"SOFTWARE\Classes\{name}"
                    },
                    Reason = CoreText.Get("Rs_R_NoFileType", "İşaret ettiği dosya türü kayıtlı değil"),
                    Target = progId,
                    Risk = Risk
                };
            }
        }
    }

    private static bool ProgIdExists(string progId)
    {
        foreach (RegistryHive hive in (RegistryHive[])[RegistryHive.LocalMachine, RegistryHive.CurrentUser])
        {
            foreach (RegistryView view in RegistryReader.BothViews)
            {
                if (RegistryReader.KeyExists(hive, view, $@"SOFTWARE\Classes\{progId}"))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>Program türlerinin bağlı olduğu COM bileşenleri.</summary>
public sealed class ProgIdClassScanner : PathValueScannerBase
{
    public override string Id => "progid-clsid";

    public override string Title =>
        CoreText.Get("Rs_ProgIdClsid_Title", "Geçersiz program türü kayıtları");

    public override string Explanation =>
        CoreText.Get("Rs_ProgIdClsid_Desc",
        "Bir program türü, kendisini açacak COM bileşenine (CLSID) işaret eder. Bileşen " +
        "kayıtlı değilse tür de çalışmaz; kayıt boşuna durur.");


    public override RiskLevel Risk => RiskLevel.Caution;

    public override bool DefaultEnabled => false;

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        foreach (RegistryHive hive in (RegistryHive[])[RegistryHive.LocalMachine, RegistryHive.CurrentUser])
        {
            using RegistryKey? classes = RegistryReader.OpenKey(hive, RegistryView.Registry64, @"SOFTWARE\Classes");

            foreach (string name in RegistryReader.SubKeyNames(classes))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Uzantılar ve CLSID dalı bu tarayıcının konusu değil.
                if (name.StartsWith('.') || name.Equals("CLSID", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using RegistryKey? progId = RegistryReader.OpenSubKey(classes, name);
                using RegistryKey? clsidKey = RegistryReader.OpenSubKey(progId, "CLSID");

                string? classId = RegistryReader.StringValue(clsidKey);

                if (string.IsNullOrWhiteSpace(classId) || !classId.StartsWith('{'))
                {
                    continue;
                }

                if (RegistryReader.ClassIdRegistered(classId, RegistryView.Registry64))
                {
                    continue;
                }

                yield return new RegistryFinding
                {
                    ScannerId = Id,
                    Location = new RegistryLocation
                    {
                        Hive = hive,
                        KeyPath = $@"SOFTWARE\Classes\{name}\CLSID"
                    },
                    Reason = CoreText.Get("Rs_R_NoComComponent", "İşaret ettiği COM bileşeni kayıtlı değil"),
                    Target = classId,
                    Risk = Risk
                };
            }
        }
    }
}

/// <summary>COM sunucularının dosyaları: InprocServer32 ve LocalServer32.</summary>
public sealed class ComServerScanner : PathValueScannerBase
{
    private static readonly string[] ServerKeys = ["InprocServer32", "LocalServer32", "InprocHandler32"];

    public override string Id => "com-servers";

    public override string Title =>
        CoreText.Get("Rs_ComServers_Title", "Kayıp COM bileşenleri");

    public override string Explanation =>
        CoreText.Get("Rs_ComServers_Desc",
        "COM bileşenleri, kendilerini sağlayan DLL veya EXE dosyasına işaret eder. Dosya " +
        "silinmişse bileşen çalışmaz; kayıt yalnızca yer kaplar ve bileşeni arayan " +
        "programları bekletir.");


    public override RiskLevel Risk => RiskLevel.Caution;

    public override bool DefaultEnabled => false;

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        foreach (RegistryHive hive in (RegistryHive[])[RegistryHive.LocalMachine, RegistryHive.CurrentUser])
        {
            foreach (RegistryView view in RegistryReader.ViewsFor(hive, @"SOFTWARE\Classes"))
            {
                using RegistryKey? clsidRoot = RegistryReader.OpenKey(hive, view, @"SOFTWARE\Classes\CLSID");

                foreach (string classId in RegistryReader.SubKeyNames(clsidRoot))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using RegistryKey? classKey = RegistryReader.OpenSubKey(clsidRoot, classId);

                    foreach (string serverKey in ServerKeys)
                    {
                        using RegistryKey? server = RegistryReader.OpenSubKey(classKey, serverKey);

                        if (server is null)
                        {
                            continue;
                        }

                        string? target = RegistryReader.StringValue(server);

                        // "mscoree.dll" gibi çıplak dosya adları sistem yolunda aranır;
                        // yol çözümleyici bunları zaten "bilinmiyor" sayıp geçiyor.
                        RegistryFinding? finding = ProbeValue(
                            new RegistryLocation
                            {
                                Hive = hive,
                                View = view,
                                KeyPath = $@"SOFTWARE\Classes\CLSID\{classId}"
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
    }
}

/// <summary>Tip kütüphaneleri: COM arayüzlerinin tanım dosyaları.</summary>
public sealed class TypeLibraryScanner : PathValueScannerBase
{
    public override string Id => "type-libraries";

    public override string Title =>
        CoreText.Get("Rs_TypeLibraries_Title", "Kırık tip kütüphaneleri");

    public override string Explanation =>
        CoreText.Get("Rs_TypeLibraries_Desc",
        "Tip kütüphaneleri, COM arayüzlerinin nasıl çağrılacağını tarif eder. Dosyası " +
        "silinmiş bir kütüphane kaydı hiçbir işe yaramaz.");


    public override RiskLevel Risk => RiskLevel.Caution;

    public override bool DefaultEnabled => false;

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        foreach (RegistryHive hive in (RegistryHive[])[RegistryHive.LocalMachine, RegistryHive.CurrentUser])
        {
            using RegistryKey? typeLibRoot = RegistryReader.OpenKey(
                hive, RegistryView.Registry64, @"SOFTWARE\Classes\TypeLib");

            foreach (string libId in RegistryReader.SubKeyNames(typeLibRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using RegistryKey? lib = RegistryReader.OpenSubKey(typeLibRoot, libId);

                foreach (string version in RegistryReader.SubKeyNames(lib))
                {
                    using RegistryKey? versionKey = RegistryReader.OpenSubKey(lib, version);

                    foreach (string locale in RegistryReader.SubKeyNames(versionKey))
                    {
                        using RegistryKey? localeKey = RegistryReader.OpenSubKey(versionKey, locale);

                        foreach (string platform in RegistryReader.SubKeyNames(localeKey))
                        {
                            // win32 / win64 alt anahtarları dosyayı gösterir; FLAGS, HELPDIR göstermez.
                            if (!platform.StartsWith("win", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            using RegistryKey? platformKey = RegistryReader.OpenSubKey(localeKey, platform);

                            RegistryFinding? finding = ProbeValue(
                                new RegistryLocation
                                {
                                    Hive = hive,
                                    KeyPath = $@"SOFTWARE\Classes\TypeLib\{libId}\{version}"
                                },
                                RegistryReader.StringValue(platformKey));

                            if (finding is not null)
                            {
                                yield return finding;
                            }
                        }
                    }
                }
            }
        }
    }
}

/// <summary>Kabuk uzantısı onay listesi: sağ tık menüsüne karışan bileşenler.</summary>
public sealed class ShellExtensionScanner : PathValueScannerBase
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved";

    public override string Id => "shell-extensions";

    public override string Title =>
        CoreText.Get("Rs_ShellExtensions_Title", "Onaylı kabuk uzantısı artıkları");

    public override string Explanation =>
        CoreText.Get("Rs_ShellExtensions_Desc",
        "Dosya Gezgini'nin sağ tık menüsüne ve önizlemelerine karışan bileşenlerin onay listesi. " +
        "Kaldırılmış programların girdileri burada kalır ve Gezgin her açılışta bunları arar.");


    public override bool RequiresAdmin => true;

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        using RegistryKey? key = RegistryReader.OpenKey(
            RegistryHive.LocalMachine, RegistryView.Registry64, KeyPath);

        foreach (string valueName in RegistryReader.ValueNames(key))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Değer adı CLSID'dir, verisi açıklamadır.
            if (!valueName.StartsWith('{') || RegistryReader.ClassIdRegistered(valueName, RegistryView.Registry64))
            {
                continue;
            }

            yield return new RegistryFinding
            {
                ScannerId = Id,
                Location = new RegistryLocation
                {
                    Hive = RegistryHive.LocalMachine,
                    KeyPath = KeyPath,
                    ValueName = valueName
                },
                Reason = CoreText.Get("Rs_R_NoComponent", "İşaret ettiği bileşen kayıtlı değil"),
                Target = RegistryReader.StringValue(key, valueName) ?? valueName,
                Risk = Risk
            };
        }
    }
}

/// <summary>Programlar ve Özellikler listesindeki ölü girdiler.</summary>
public sealed class UninstallEntryScanner : PathValueScannerBase
{
    private static readonly (RegistryHive Hive, string Path)[] Locations =
    [
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
    ];

    public override string Id => "uninstall-entries";

    public override string Title =>
        CoreText.Get("Rs_UninstallEntries_Title", "Ölü kaldırma girdileri");

    public override string Explanation =>
        CoreText.Get("Rs_UninstallEntries_Desc",
        "Programlar ve Özellikler listesini besleyen kayıtlar. Kaldırma programı silinmişse " +
        "girdi listede görünür ama kaldırılamaz — \"bu program zaten kaldırılmış\" hatası verir.");

    public override RiskLevel Risk => RiskLevel.Caution;

    public override bool DefaultEnabled => false;

    public override IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken)
    {
        foreach ((RegistryHive hive, string keyPath) in Locations)
        {
            foreach (RegistryView view in RegistryReader.ViewsFor(hive, keyPath))
            {
                using RegistryKey? root = RegistryReader.OpenKey(hive, view, keyPath);

                foreach (string entryName in RegistryReader.SubKeyNames(root))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using RegistryKey? entry = RegistryReader.OpenSubKey(root, entryName);

                    if (entry is null)
                    {
                        continue;
                    }

                    // Windows güncellemeleri ve sistem bileşenleri bu listede görünür ama
                    // kaldırma dizesi taşımaz; bunları ölü saymak yanlış olur.
                    string? uninstallString = RegistryReader.StringValue(entry, "UninstallString");

                    if (string.IsNullOrWhiteSpace(uninstallString))
                    {
                        continue;
                    }

                    // MSI kaldırmaları msiexec üzerinden çalışır, dosya yolu taşımaz.
                    if (uninstallString.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    RegistryFinding? finding = ProbeValue(
                        new RegistryLocation
                        {
                            Hive = hive,
                            View = view,
                            KeyPath = $@"{keyPath}\{entryName}"
                        },
                        uninstallString,
                        CoreText.Get("Rs_R_NoUninstaller", "Kaldırma programı yok"));

                    if (finding is not null)
                    {
                        yield return finding with
                        {
                            Target = RegistryReader.StringValue(entry, "DisplayName") ?? finding.Target
                        };
                    }
                }
            }
        }
    }
}
