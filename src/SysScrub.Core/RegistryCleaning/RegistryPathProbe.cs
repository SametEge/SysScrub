using System.Text.RegularExpressions;

// Ad alanı bilerek "…Core.Registry" değil: o isim, Microsoft.Win32.Registry sınıfını
// gölgeleyip bu klasördeki her dosyada derleme hatası üretiyor.
namespace SysScrub.Core.RegistryCleaning;

/// <summary>
/// Registry değerlerindeki dosya yollarını çözer ve gerçekten var olup olmadıklarını söyler.
///
/// Registry temizleyicilerin geçerli kayıtları silmesinin bir numaralı sebebi bu adım.
/// Kayıtlarda yol şu biçimlerin hepsinde karşımıza çıkıyor:
///
///   C:\Program Files\App\app.exe
///   "C:\Program Files\App\app.exe" --flag
///   %SystemRoot%\system32\shell32.dll
///   C:\Windows\System32\shell32.dll,-123        (kaynak indeksi)
///   @%SystemRoot%\system32\foo.dll,-2           (dolaylı dize)
///   rundll32.exe foo.dll,EntryPoint
///   {12345678-...}                              (hiç yol değil)
///
/// Bunlardan birini yanlış okuyup "dosya yok" demek, çalışan bir kaydı silmek demek.
/// Bu yüzden emin olamadığımız her durumda <see cref="ProbeResult.Unknown"/> dönüyoruz
/// ve tarayıcılar bunu bulgu saymıyor.
/// </summary>
public static class RegistryPathProbe
{
    private static readonly Regex ResourceIndexSuffix = new(@",\s*-?\d+\s*$", RegexOptions.Compiled);

    private static readonly string[] IgnoredPrefixes =
    [
        "http://", "https://", "ftp://", "mailto:", "shell:", "ms-", "res://"
    ];

    /// <summary>Bir registry değerinin işaret ettiği dosyanın durumu.</summary>
    public enum ProbeResult
    {
        /// <summary>Dosya var; kayıt geçerli.</summary>
        Exists,

        /// <summary>Dosya kesin olarak yok; kayıt ölü.</summary>
        Missing,

        /// <summary>Yol çözülemedi ya da dosya yolu değil. Bulgu sayılmaz.</summary>
        Unknown
    }

    /// <summary>Değerin işaret ettiği dosyayı çözer ve varlığını denetler.</summary>
    public static ProbeResult Probe(string? rawValue, out string resolvedPath)
    {
        resolvedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return ProbeResult.Unknown;
        }

        string? candidate = ExtractPath(rawValue);

        if (candidate is null)
        {
            return ProbeResult.Unknown;
        }

        resolvedPath = candidate;

        if (FileOrDirectoryExists(candidate))
        {
            return ProbeResult.Exists;
        }

        // 32-bit kayıtlar System32 yazar ama dosya aslında SysWOW64'te olur.
        // 64-bit süreçten bakınca yönlendirme yapılmadığı için elle denemek gerekiyor.
        if (TryArchitectureTwin(candidate, out string twin) && FileOrDirectoryExists(twin))
        {
            resolvedPath = twin;
            return ProbeResult.Exists;
        }

        return ProbeResult.Missing;
    }

    /// <summary>Ham değerden dosya yolunu ayıklar. Yol değilse null döner.</summary>
    public static string? ExtractPath(string rawValue)
    {
        string value = rawValue.Trim();

        if (value.Length == 0)
        {
            return null;
        }

        // "@" öneki dolaylı dize kaynağını işaret eder: @%SystemRoot%\...\foo.dll,-2
        if (value.StartsWith('@'))
        {
            value = value[1..];
        }

        foreach (string prefix in IgnoredPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        // GUID veya ProgID gibi yol olmayan değerler.
        if (value.StartsWith('{') || value.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        value = value.StartsWith('"') ? ExtractQuoted(value) : StripArguments(value);

        if (value.Length == 0)
        {
            return null;
        }

        // Sondaki kaynak indeksi (",-123") yolun parçası değil.
        value = ResourceIndexSuffix.Replace(value, string.Empty).Trim();

        // rundll32 gibi başlatıcılar: asıl hedef virgülden önceki modül.
        int comma = value.IndexOf(',');

        if (comma > 0 && !value.Contains(",\\", StringComparison.Ordinal))
        {
            value = value[..comma].Trim();
        }

        value = Environment.ExpandEnvironmentVariables(value).Trim();

        // Genişletilememiş değişken kaldıysa yolu bilmiyoruz demektir.
        if (value.Contains('%'))
        {
            return null;
        }

        if (value.Length == 0 || !Path.IsPathRooted(value))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string ExtractQuoted(string value)
    {
        int closing = value.IndexOf('"', 1);

        return closing > 1 ? value[1..closing] : value.Trim('"');
    }

    /// <summary>
    /// Tırnaksız komut satırında yolu argümanlardan ayırır.
    /// Boşluklu yollar tırnaksız yazıldığında hangi parçanın yol olduğu belirsiz;
    /// bu yüzden var olan en uzun önek aranıyor.
    /// </summary>
    private static string StripArguments(string value)
    {
        int switchIndex = value.IndexOf(" /", StringComparison.Ordinal);
        int dashIndex = value.IndexOf(" -", StringComparison.Ordinal);

        int cut = (switchIndex, dashIndex) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => dashIndex,
            (_, < 0) => switchIndex,
            _ => Math.Min(switchIndex, dashIndex)
        };

        string trimmed = cut > 0 ? value[..cut].Trim() : value;

        if (FileOrDirectoryExists(trimmed) || !trimmed.Contains(' '))
        {
            return trimmed;
        }

        // "C:\Program Files\App\app.exe bir argüman" — var olan en uzun öneki bul.
        string[] parts = trimmed.Split(' ');

        for (int take = parts.Length; take > 0; take--)
        {
            string candidate = string.Join(' ', parts, 0, take);
            string expanded;

            try
            {
                expanded = Environment.ExpandEnvironmentVariables(candidate);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (FileOrDirectoryExists(expanded))
            {
                return candidate;
            }
        }

        return trimmed;
    }

    private static bool FileOrDirectoryExists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>System32 ↔ SysWOW64 karşılığını üretir.</summary>
    private static bool TryArchitectureTwin(string path, out string twin)
    {
        twin = string.Empty;

        string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        string system32 = Path.Combine(systemRoot, "System32");
        string sysWow64 = Path.Combine(systemRoot, "SysWOW64");

        if (path.StartsWith(system32, StringComparison.OrdinalIgnoreCase))
        {
            twin = sysWow64 + path[system32.Length..];
            return true;
        }

        if (path.StartsWith(sysWow64, StringComparison.OrdinalIgnoreCase))
        {
            twin = system32 + path[sysWow64.Length..];
            return true;
        }

        return false;
    }
}
