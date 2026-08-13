using System.Security;
using Microsoft.Win32;

namespace SysScrub.Core.Startup;

/// <summary>
/// Windows'un başlangıç öğelerini açıp kapattığı mekanizma.
///
/// Görev Yöneticisi bir öğeyi devre dışı bırakırken Run kaydını SİLMEZ; ayrı bir
/// "StartupApproved" anahtarına 12 baytlık bir durum yazar. Biz de aynısını yapıyoruz.
/// Bunun iki faydası var: kayıt yerinde kaldığı için işlem her zaman geri alınabilir,
/// ve Görev Yöneticisi ile senkron kalıyoruz — iki araç birbirini ezmiyor.
///
/// Bayt düzeni: ilk bayt durum (02/06 açık, 03/07 kapalı), sonraki 4 bayt dolgu,
/// son 8 bayt kapatılma zamanının FILETIME değeri.
/// </summary>
public sealed class StartupApprovedStore
{
    private const string WindowsBasePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private const byte EnabledFlag = 0x02;
    private const byte DisabledFlag = 0x03;

    private readonly string _basePath;

    /// <param name="basePath">
    /// Onay anahtarının kökü. Yalnızca testler kendi kum havuzunu vermek için değiştirir;
    /// gerçek kullanımda Windows'un anahtarı kullanılır.
    /// </param>
    public StartupApprovedStore(string? basePath = null) => _basePath = basePath ?? WindowsBasePath;

    /// <summary>StartupApproved altındaki alt anahtar adları.</summary>
    public enum ApprovalScope
    {
        Run,
        Run32,
        StartupFolder
    }

    /// <summary>Bir öğenin açık olup olmadığı. Kayıt yoksa öğe açıktır.</summary>
    public bool IsEnabled(RegistryHive hive, ApprovalScope scope, string valueName)
    {
        byte[]? state = ReadState(hive, scope, valueName);

        // Kayıt yoksa Windows öğeyi açık sayar.
        return state is null || state.Length == 0 || (state[0] & 0x01) == 0;
    }

    /// <summary>Öğeyi açar veya kapatır.</summary>
    public bool SetEnabled(RegistryHive hive, ApprovalScope scope, string valueName, bool enabled)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey key = baseKey.CreateSubKey($@"{_basePath}\{scope}", writable: true);

            key.SetValue(valueName, BuildState(enabled), RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Kapatılma zamanı; hiç kapatılmamışsa null.</summary>
    public DateTime? DisabledAt(RegistryHive hive, ApprovalScope scope, string valueName)
    {
        byte[]? state = ReadState(hive, scope, valueName);

        if (state is null || state.Length < 12 || (state[0] & 0x01) == 0)
        {
            return null;
        }

        try
        {
            long fileTime = BitConverter.ToInt64(state, 4);

            return fileTime > 0 ? DateTime.FromFileTime(fileTime) : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[] BuildState(bool enabled)
    {
        var state = new byte[12];
        state[0] = enabled ? EnabledFlag : DisabledFlag;

        if (!enabled)
        {
            // Görev Yöneticisi kapatma zamanını burada saklıyor; aynı biçimi koruyoruz.
            BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(state, 4);
        }

        return state;
    }

    private byte[]? ReadState(RegistryHive hive, ApprovalScope scope, string valueName)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey($@"{_basePath}\{scope}");

            return key?.GetValue(valueName) as byte[];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return null;
        }
    }
}
