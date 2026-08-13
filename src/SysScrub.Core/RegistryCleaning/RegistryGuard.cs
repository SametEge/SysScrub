using Microsoft.Win32;
using SysScrub.Core.Formatting;

namespace SysScrub.Core.RegistryCleaning;

public enum RegistryDenialReason
{
    None = 0,

    /// <summary>Yol boş, kovan desteklenmiyor ya da anahtar yolu geçersiz.</summary>
    InvalidLocation,

    /// <summary>Kovanın kökü ya da köke çok yakın bir dal; silinemez.</summary>
    TooCloseToRoot,

    /// <summary>İşletim sisteminin çalışması için gereken dal.</summary>
    ProtectedSystemKey,

    /// <summary>Grup ilkeleri ve güvenlik ayarları.</summary>
    ProtectedPolicy,

    /// <summary>Tarayıcıların yetkili olduğu alanların dışında.</summary>
    OutsideAllowedScope
}

public readonly record struct RegistryVerdict(bool IsAllowed, RegistryDenialReason Reason)
{
    public static RegistryVerdict Allow { get; } = new(true, RegistryDenialReason.None);

    public static RegistryVerdict Deny(RegistryDenialReason reason) => new(false, reason);

    public string Describe() => Reason switch
    {
        RegistryDenialReason.None => CoreText.Get("Gv_Allowed", "İzin verildi."),
        RegistryDenialReason.InvalidLocation => CoreText.Get("Rg_Invalid", "Geçersiz registry konumu."),
        RegistryDenialReason.TooCloseToRoot => CoreText.Get("Rg_TooShallow", "Kovanın köküne çok yakın; silinemez."),
        RegistryDenialReason.ProtectedSystemKey => CoreText.Get("Rg_Protected", "Windows'un çalışması için gereken korumalı dal."),
        RegistryDenialReason.ProtectedPolicy => CoreText.Get("Rg_Policy", "Grup ilkesi veya güvenlik ayarı."),
        RegistryDenialReason.OutsideAllowedScope => CoreText.Get("Rg_OutOfScope", "Temizleyicinin yetkili olduğu alanların dışında."),
        _ => CoreText.Get("Gv_UnknownReason", "Bilinmeyen sebeple reddedildi.")
    };
}

/// <summary>
/// Registry tarafının güvenlik sınırı. Dosya tarafındaki SafetyGuard ile aynı ilke:
/// tarayıcılara güvenilmez, her silme adayı yolun kendisine bakılarak denetlenir.
///
/// İki katman var:
///   1. Yasak ağaçlar — hiçbir koşulda dokunulmaz
///   2. İzinli kapsam — tarayıcıların yetkili olduğu dallar; dışına çıkılamaz
///
/// İkisi birden olmasının sebebi: yalnızca yasak listesi tutmak, listeye eklemeyi
/// unuttuğumuz her yeni tehlikeli dalı açıkta bırakır. İzinli kapsam ise varsayılanı
/// "hayır" yapıyor.
///
/// HKLM\SYSTEM tamamen kapsam dışı. Güvenlik duvarı kuralları orada durduğu için
/// o tarayıcı v1'e alınmadı: en tehlikeli kovanın içine istisna açmak, güvenlik
/// katmanının anlamını yitirmesi demek.
/// </summary>
public sealed class RegistryGuard
{
    /// <summary>Bu derinliğin altındaki anahtarlar tek başına silinemez.</summary>
    private const int MinimumDepth = 2;

    private static readonly ProtectedTree[] ProtectedTrees =
    [
        // ---- Çekirdek işletim sistemi ----
        new(RegistryHive.LocalMachine, "SYSTEM", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, "SECURITY", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, "SAM", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, "HARDWARE", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, "BCD00000000", RegistryDenialReason.ProtectedSystemKey),

        // ---- Bileşen bakımı ve yan yana kurulum ----
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\SideBySide", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate", RegistryDenialReason.ProtectedSystemKey),

        // ---- Çalışma zamanları ve güvenlik ----
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\.NETFramework", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\NET Framework Setup", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Cryptography", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\SystemCertificates", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\SystemCertificates", RegistryDenialReason.ProtectedSystemKey),

        // ---- İlkeler ----
        new(RegistryHive.LocalMachine, @"SOFTWARE\Policies", RegistryDenialReason.ProtectedPolicy),
        new(RegistryHive.CurrentUser, @"SOFTWARE\Policies", RegistryDenialReason.ProtectedPolicy),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies", RegistryDenialReason.ProtectedPolicy),
        new(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies", RegistryDenialReason.ProtectedPolicy),

        // ---- Oturum ve profil ----
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList", RegistryDenialReason.ProtectedSystemKey),
        new(RegistryHive.CurrentUser, "Volatile Environment", RegistryDenialReason.ProtectedSystemKey)
    ];

    /// <summary>
    /// Tarayıcıların yetkili olduğu dallar. Bir bulgu bunlardan birinin altında değilse
    /// ne kadar "ölü" görünürse görünsün silinmez.
    /// </summary>
    private static readonly AllowedScope[] AllowedScopes =
    [
        // Dosya türü kayıtları ve COM
        new(RegistryHive.LocalMachine, @"SOFTWARE\Classes"),
        new(RegistryHive.CurrentUser, @"SOFTWARE\Classes"),

        // Paylaşılan DLL sayaçları
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs"),

        // Uygulama yolları
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),

        // Kaldırma girdileri
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        new(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),

        // Başlangıç kayıtları
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
        new(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        new(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),

        // Kabuk uzantısı onay listesi
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"),

        // Yükleyici klasör kayıtları
        new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders"),

        // Son çalıştırılan uygulama adları
        new(RegistryHive.CurrentUser, @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache"),

        // Ses olayları
        new(RegistryHive.CurrentUser, @"AppEvents\Schemes\Apps"),

        // Testlerin kendi kum havuzu — gerçek sistemde bu dal oluşmaz
        new(RegistryHive.CurrentUser, @"SOFTWARE\SysScrub.Tests")
    ];

    /// <summary>Tanılama ve testler için açık.</summary>
    public static IReadOnlyList<string> AllowedScopeNames =>
        AllowedScopes.Select(s => $"{s.Hive}\\{s.KeyPath}").ToArray();

    public RegistryVerdict Inspect(RegistryLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);

        string keyPath = Normalize(location.KeyPath);

        if (keyPath.Length == 0)
        {
            return RegistryVerdict.Deny(RegistryDenialReason.InvalidLocation);
        }

        // HKCR birleştirilmiş görünüm; hangi kovana yazıldığı belirsiz olduğu için kabul edilmiyor.
        if (location.Hive is not (RegistryHive.LocalMachine or RegistryHive.CurrentUser))
        {
            return RegistryVerdict.Deny(RegistryDenialReason.InvalidLocation);
        }

        // Yasak ağaçlar önce: izinli kapsamla kesişse bile geçemez.
        foreach (ProtectedTree tree in ProtectedTrees)
        {
            if (tree.Hive == location.Hive && IsUnder(keyPath, tree.KeyPath))
            {
                return RegistryVerdict.Deny(tree.Reason);
            }
        }

        if (!AllowedScopes.Any(scope => scope.Hive == location.Hive && IsUnder(keyPath, scope.KeyPath)))
        {
            return RegistryVerdict.Deny(RegistryDenialReason.OutsideAllowedScope);
        }

        // Anahtarın tamamı siliniyorsa köke yakın bir dal olmadığından emin ol.
        if (location.TargetsWholeKey && Depth(keyPath) < MinimumDepth)
        {
            return RegistryVerdict.Deny(RegistryDenialReason.TooCloseToRoot);
        }

        // İzinli kapsamın kendisi silinemez: içindekiler temizlenir, dal ayakta kalır.
        if (location.TargetsWholeKey &&
            AllowedScopes.Any(scope => scope.Hive == location.Hive && Equals(keyPath, scope.KeyPath)))
        {
            return RegistryVerdict.Deny(RegistryDenialReason.TooCloseToRoot);
        }

        return RegistryVerdict.Allow;
    }

    private static string Normalize(string keyPath) =>
        keyPath.Trim().Trim('\\');

    private static bool Equals(string path, string other) =>
        string.Equals(Normalize(path), Normalize(other), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root)
    {
        string normalizedRoot = Normalize(root);

        if (string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Ayırıcı eklenmezse "SOFTWARE\ClassesX" yolu "SOFTWARE\Classes" altında sanılır.
        return path.StartsWith(normalizedRoot + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static int Depth(string keyPath) =>
        keyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;

    private readonly record struct ProtectedTree(RegistryHive Hive, string KeyPath, RegistryDenialReason Reason);

    private readonly record struct AllowedScope(RegistryHive Hive, string KeyPath);
}
