using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace SysScrub.Core.Updates;

/// <summary>
/// Sürüm numarası ve karşılaştırması.
///
/// Anlamsal sürümlemenin bize gereken kadarı: üç sayı ve isteğe bağlı ön yayın
/// etiketi. Ön yayın, aynı sayıların kararlı sürümünden <b>küçüktür</b>
/// (0.13.0-alpha &lt; 0.13.0) — tersi olsaydı alfa kullanan kişi kararlı sürüme
/// hiç geçemezdi.
/// </summary>
public readonly record struct AppVersion : IComparable<AppVersion>
{
    public AppVersion(int major, int minor, int patch, string preRelease = "")
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease ?? string.Empty;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>"alpha", "rc.1" gibi; kararlı sürümlerde boş.</summary>
    public string PreRelease { get; }

    public bool IsPreRelease => PreRelease.Length > 0;

    public bool IsEmpty => Major == 0 && Minor == 0 && Patch == 0 && !IsPreRelease;

    /// <summary>
    /// "v0.13.0-alpha", "0.13.0", "1.2.3-rc.1+abc123" hepsini kabul eder.
    /// Etiket adları da bu yoldan geçiyor, o yüzden baştaki "v" düşürülüyor.
    /// </summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> span = text.Trim();

        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
        {
            span = span[1..];
        }

        // Derleme üstverisi (+commit) karşılaştırmaya girmez, sessizce atılır.
        int plus = span.IndexOf('+');

        if (plus >= 0)
        {
            span = span[..plus];
        }

        string preRelease = string.Empty;
        int dash = span.IndexOf('-');

        if (dash >= 0)
        {
            preRelease = span[(dash + 1)..].ToString();
            span = span[..dash];
        }

        Span<Range> parts = stackalloc Range[4];
        int count = span.Split(parts, '.');

        if (count is < 1 or > 3)
        {
            return false;
        }

        Span<int> numbers = stackalloc int[3];

        for (int i = 0; i < 3; i++)
        {
            if (i >= count)
            {
                numbers[i] = 0;
                continue;
            }

            if (!int.TryParse(span[parts[i]], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                return false;
            }

            numbers[i] = value;
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2], preRelease);
        return true;
    }

    public static AppVersion Parse(string text) =>
        TryParse(text, out AppVersion version)
            ? version
            : throw new FormatException($"Sürüm numarası çözümlenemedi: {text}");

    /// <summary>
    /// Çalışan derlemenin sürümü. Bilgilendirici sürüm "0.13.0-alpha+commit"
    /// biçiminde geldiği için ön yayın etiketi buradan okunuyor; dosya sürümünde
    /// o etiket yok.
    /// </summary>
    public static AppVersion FromAssembly(Assembly assembly)
    {
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (TryParse(informational, out AppVersion version))
        {
            return version;
        }

        Version? fallback = assembly.GetName().Version;

        return fallback is null
            ? default
            : new AppVersion(fallback.Major, fallback.Minor, fallback.Build);
    }

    public int CompareTo(AppVersion other)
    {
        int result = Major.CompareTo(other.Major);

        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);

        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);

        return result != 0 ? result : ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>
    /// Ön yayın etiketleri nokta ile bölünüp sırayla karşılaştırılır: sayısal
    /// parçalar sayı olarak (rc.2 &gt; rc.10 hatasına düşmemek için), diğerleri
    /// harf sırasına göre. Sayısal parça, harfli parçadan küçüktür.
    /// </summary>
    private static int ComparePreRelease(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0)
        {
            return 0;
        }

        // Kararlı sürüm her zaman büyüktür.
        if (left.Length == 0)
        {
            return 1;
        }

        if (right.Length == 0)
        {
            return -1;
        }

        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');

        for (int i = 0; i < Math.Max(leftParts.Length, rightParts.Length); i++)
        {
            if (i >= leftParts.Length)
            {
                return -1;
            }

            if (i >= rightParts.Length)
            {
                return 1;
            }

            bool leftNumeric = int.TryParse(leftParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int leftValue);
            bool rightNumeric = int.TryParse(rightParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int rightValue);

            int result = (leftNumeric, rightNumeric) switch
            {
                (true, true) => leftValue.CompareTo(rightValue),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(leftParts[i], rightParts[i])
            };

            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        IsPreRelease
            ? $"{Major}.{Minor}.{Patch}-{PreRelease}"
            : $"{Major}.{Minor}.{Patch}";

    [SuppressMessage("Design", "CA1024", Justification = "Kayıt yapısı zaten değer türü.")]
    public string ToTag() => "v" + ToString();
}
