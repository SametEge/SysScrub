using System.Text.RegularExpressions;
using SysScrub.Core.RegistryCleaning;

namespace SysScrub.Core.Programs;

/// <summary>Çalıştırılabilir dosya ve argümanlarına ayrılmış kaldırma komutu.</summary>
public readonly record struct UninstallCommand(string FileName, string Arguments)
{
    // default(UninstallCommand) alanları null bırakıyor; ayrıştırılamayan komutlar
    // bu değeri döndürdüğü için boşluk denetimi null'a da dayanmak zorunda.
    public bool IsValid => !string.IsNullOrEmpty(FileName);
}

/// <summary>
/// Kaldırma komutlarını çalıştırılabilir dosya + argüman olarak ayırır.
///
/// Registry'deki komutlar tek bir metin ve biçimleri hiç tutarlı değil:
///
///   "C:\Program Files\Git\unins000.exe"                    tırnaklı, argümansız
///   "C:\Program Files\Git\unins000.exe" /SILENT             tırnaklı + argüman
///   C:\Program Files\Android\Studio\uninstall.exe           TIRNAKSIZ, boşluklu
///   C:\Program Files\AMD\bin\Setup.exe /U {GUID}            tırnaksız + boşluk + argüman
///   MsiExec.exe /I{90160000-...}                            MSI, ürün kodu bitişik
///
/// Tırnaksız ve boşluklu yolu argümanından ayırmak tahmin gerektiriyor; hangi önekin
/// gerçekten var olan bir dosya olduğuna bakıyoruz. Yanlış bölmek, kaldırıcıyı hiç
/// çalıştıramamak demek.
/// </summary>
public static class UninstallCommandLine
{
    private static readonly Regex MsiProductCode = new(
        @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}",
        RegexOptions.Compiled);

    /// <summary>Komutu çalıştırılabilir dosya ve argümanlarına ayırır.</summary>
    public static UninstallCommand Parse(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return default;
        }

        string value = command.Trim();

        if (value.StartsWith('"'))
        {
            int closing = value.IndexOf('"', 1);

            return closing > 1
                ? new UninstallCommand(value[1..closing], value[(closing + 1)..].Trim())
                : new UninstallCommand(value.Trim('"'), string.Empty);
        }

        // Tırnaksız: var olan en uzun öneki dosya yolu kabul ediyoruz.
        string[] parts = value.Split(' ');

        for (int take = parts.Length; take > 0; take--)
        {
            string candidate = string.Join(' ', parts, 0, take);
            string expanded = Expand(candidate);

            if (File.Exists(expanded))
            {
                return new UninstallCommand(expanded, string.Join(' ', parts, take, parts.Length - take).Trim());
            }
        }

        // Dosya bulunamadı: ilk boşluğa kadarını komut sayıyoruz. "MsiExec.exe /X{...}"
        // gibi PATH üzerinden çözülen komutlar bu dalda doğru ayrılıyor.
        int space = value.IndexOf(' ');

        return space > 0
            ? new UninstallCommand(Expand(value[..space]), value[(space + 1)..].Trim())
            : new UninstallCommand(Expand(value), string.Empty);
    }

    /// <summary>Komut MSI kaldırması mı — sessiz moda çevirebildiğimiz tek tür.</summary>
    public static bool IsMsi(string? command) =>
        command is not null &&
        command.TrimStart().StartsWith("msiexec", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// MSI komutunu sessiz kaldırmaya çevirir.
    ///
    /// <c>/norestart</c> bilerek ekleniyor: sessiz MSI kaldırması varsayılan olarak
    /// bilgisayarı sormadan yeniden başlatabiliyor. Kullanıcının açık dosyalarını
    /// kaybetmesindense yeniden başlatmayı ona bırakıyoruz.
    /// </summary>
    public static UninstallCommand? ToSilentMsi(string? command)
    {
        if (!IsMsi(command))
        {
            return null;
        }

        Match match = MsiProductCode.Match(command!);

        if (!match.Success)
        {
            return null;
        }

        return new UninstallCommand("msiexec.exe", $"/x {match.Value} /qn /norestart");
    }

    private static string Expand(string value)
    {
        try
        {
            return Environment.ExpandEnvironmentVariables(value).Trim('"');
        }
        catch (ArgumentException)
        {
            return value;
        }
    }

    /// <summary>Komutun işaret ettiği dosya gerçekten var mı.</summary>
    public static bool TargetExists(string? command)
    {
        UninstallCommand parsed = Parse(command);

        if (!parsed.IsValid)
        {
            return false;
        }

        // PATH üzerinden çözülen komutlar (msiexec gibi) yol içermiyor; onlara var diyoruz.
        return !Path.IsPathRooted(parsed.FileName) ||
               RegistryPathProbe.Probe(parsed.FileName, out _) != RegistryPathProbe.ProbeResult.Missing;
    }
}
