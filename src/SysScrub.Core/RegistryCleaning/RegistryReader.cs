using System.Security;
using Microsoft.Win32;

namespace SysScrub.Core.RegistryCleaning;

/// <summary>
/// Registry okuma yardımcıları. Tarayıcılar bunları kullanır.
///
/// Her çağrı erişim hatalarını yutar ve boş sonuç döner: yetkimizin yetmediği
/// bir dal yüzünden tüm tarama düşmemeli. Okunamayan yer zaten temizlenmeyecek.
/// </summary>
public static class RegistryReader
{
    /// <summary>64-bit ve 32-bit görünümlerin ikisi; aynı anahtar iki yerde farklı içerik taşıyabilir.</summary>
    public static readonly RegistryView[] BothViews = [RegistryView.Registry64, RegistryView.Registry32];

    private static readonly RegistryView[] SingleView = [RegistryView.Registry64];

    /// <summary>
    /// Bir yol için taranması gereken görünümler.
    ///
    /// WOW64 registry yönlendirmesi yalnızca HKLM\SOFTWARE ve HKCU\Software\Classes
    /// altında geçerli. Yönlendirilmeyen bir yolda iki görünüm aynı fiziksel anahtarı
    /// gösterir; ikisini de taramak aynı kaydı iki kez bulmak demektir.
    /// </summary>
    public static IReadOnlyList<RegistryView> ViewsFor(RegistryHive hive, string keyPath) => hive switch
    {
        RegistryHive.LocalMachine when keyPath.StartsWith("SOFTWARE", StringComparison.OrdinalIgnoreCase)
            => BothViews,
        RegistryHive.CurrentUser when keyPath.StartsWith(@"SOFTWARE\Classes", StringComparison.OrdinalIgnoreCase)
            => BothViews,
        _ => SingleView
    };

    public static RegistryKey? OpenKey(RegistryHive hive, RegistryView view, string keyPath)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            return baseKey.OpenSubKey(keyPath, writable: false);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> SubKeyNames(RegistryKey? key)
    {
        if (key is null)
        {
            return [];
        }

        try
        {
            return key.GetSubKeyNames();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> ValueNames(RegistryKey? key)
    {
        if (key is null)
        {
            return [];
        }

        try
        {
            return key.GetValueNames();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    /// <summary>Değeri dize olarak okur. Ortam değişkenleri bilerek genişletilmez.</summary>
    public static string? StringValue(RegistryKey? key, string? valueName = null)
    {
        if (key is null)
        {
            return null;
        }

        try
        {
            return key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public static RegistryKey? OpenSubKey(RegistryKey? key, string name)
    {
        if (key is null)
        {
            return null;
        }

        try
        {
            return key.OpenSubKey(name, writable: false);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Bir anahtarın var olup olmadığı.</summary>
    public static bool KeyExists(RegistryHive hive, RegistryView view, string keyPath)
    {
        using RegistryKey? key = OpenKey(hive, view, keyPath);
        return key is not null;
    }

    /// <summary>
    /// CLSID kaydı gerçekten var mı. HKLM ve HKCU'nun ikisine de bakılır:
    /// kullanıcı başına kayıtlı COM bileşenleri yalnızca HKCU'da durur.
    /// </summary>
    public static bool ClassIdRegistered(string classId, RegistryView view)
    {
        foreach (RegistryHive hive in (RegistryHive[])[RegistryHive.LocalMachine, RegistryHive.CurrentUser])
        {
            if (KeyExists(hive, view, $@"SOFTWARE\Classes\CLSID\{classId}"))
            {
                return true;
            }
        }

        // WOW6432Node altındaki 32-bit kayıtlar 64-bit görünümden görünmez.
        return view == RegistryView.Registry64 && ClassIdRegistered(classId, RegistryView.Registry32);
    }
}
