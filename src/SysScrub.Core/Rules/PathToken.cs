namespace SysScrub.Core.Rules;

/// <summary>
/// Kuralların kullanabileceği sembolik kökler.
///
/// Kurallar asla mutlak yol içermez. Bunun iki sebebi var: makineden makineye
/// değişen yolları taşınabilir kılmak, ve daha önemlisi bir kuralın izin verilen
/// alanın dışına çıkmasını yapısal olarak imkânsız hale getirmek.
/// </summary>
public enum PathToken
{
    /// <summary>%TEMP% — oturum açan kullanıcının geçici klasörü.</summary>
    UserTemp,

    /// <summary>%SystemRoot%\Temp — sistem geneli geçici klasör, yönetici hakkı ister.</summary>
    WindowsTemp,

    /// <summary>%LOCALAPPDATA% — uygulama önbelleklerinin çoğu burada.</summary>
    LocalAppData,

    /// <summary>%APPDATA% — dolaşan kullanıcı verisi; burada temizlik daha dikkatli yapılır.</summary>
    RoamingAppData,

    /// <summary>%ProgramData% — makine geneli uygulama verisi.</summary>
    ProgramData,

    /// <summary>%USERPROFILE%</summary>
    UserProfile,

    /// <summary>%SystemRoot% — genelde C:\Windows.</summary>
    SystemRoot,

    /// <summary>Windows'un kurulu olduğu sürücünün kökü.</summary>
    SystemDrive,

    /// <summary>%ProgramFiles%</summary>
    ProgramFiles,

    /// <summary>%ProgramFiles(x86)%</summary>
    ProgramFilesX86,

    /// <summary>Kullanıcının İndirilenler klasörü — yalnızca raporlanır, otomatik silinmez.</summary>
    Downloads,

    /// <summary>Belgeler — SafetyGuard tarafından korunur, kural kökü olarak kullanılamaz.</summary>
    Documents,

    /// <summary>Masaüstü — SafetyGuard tarafından korunur.</summary>
    Desktop,

    /// <summary>Bağlı tüm sabit sürücülerin kökü (Geri Dönüşüm Kutusu gibi disk başına kurallar için).</summary>
    AllFixedDrives
}
