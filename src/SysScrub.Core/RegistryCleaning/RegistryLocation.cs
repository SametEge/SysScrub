using Microsoft.Win32;

namespace SysScrub.Core.RegistryCleaning;

/// <summary>
/// Registry'de tek bir konum: kovan + görünüm + anahtar yolu, isteğe bağlı değer adı.
///
/// HKEY_CLASSES_ROOT bilerek kullanılmıyor. O, HKLM\SOFTWARE\Classes ile
/// HKCU\Software\Classes'ın birleştirilmiş görünümü; oradan silmek hangi kovana
/// yazıldığını belirsiz bırakır. Tarayıcılar iki kovanı ayrı ayrı geziyor.
/// </summary>
public sealed record RegistryLocation
{
    public required RegistryHive Hive { get; init; }

    public RegistryView View { get; init; } = RegistryView.Registry64;

    /// <summary>Kovan altındaki yol. Başında ve sonunda ters eğik çizgi olmaz.</summary>
    public required string KeyPath { get; init; }

    /// <summary>Silinecek değer. Null ise anahtarın kendisi hedeflenir.</summary>
    public string? ValueName { get; init; }

    public bool TargetsWholeKey => ValueName is null;

    public string HiveName => Hive switch
    {
        RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
        RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
        RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        RegistryHive.Users => "HKEY_USERS",
        RegistryHive.CurrentConfig => "HKEY_CURRENT_CONFIG",
        _ => Hive.ToString()
    };

    public string ShortHiveName => Hive switch
    {
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.Users => "HKU",
        RegistryHive.CurrentConfig => "HKCC",
        _ => Hive.ToString()
    };

    /// <summary>.reg dosyalarında ve karşılaştırmalarda kullanılan tam yol.</summary>
    public string FullPath => $"{HiveName}\\{KeyPath}";

    /// <summary>Arayüzde gösterilen kısa gösterim.</summary>
    public string DisplayPath => ValueName is null
        ? $"{ShortHiveName}\\{KeyPath}"
        : $"{ShortHiveName}\\{KeyPath}  →  {ValueName}";

    /// <summary>32-bit görünümdeki kayıtlar arayüzde ayırt edilebilsin diye.</summary>
    public string ViewLabel => View == RegistryView.Registry32 ? "32-bit" : string.Empty;

    public override string ToString() => DisplayPath;
}
