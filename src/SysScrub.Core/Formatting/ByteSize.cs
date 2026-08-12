using System.Globalization;

namespace SysScrub.Core.Formatting;

/// <summary>
/// Bayt değerlerini insan okuyabilir metne çevirir.
///
/// Windows kabuğuyla tutarlı olsun diye 1024 tabanı kullanılır (Explorer da öyle yapar);
/// aksi halde kullanıcı "SysScrub 500 GB diyor ama Windows 465 GB diyor" der.
/// </summary>
public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes, int decimals = 1) => Format((double)bytes, decimals);

    public static string Format(ulong bytes, int decimals = 1) => Format((double)bytes, decimals);

    public static string Format(double bytes, int decimals = 1)
    {
        if (double.IsNaN(bytes) || bytes <= 0)
        {
            return "0 B";
        }

        int unit = 0;

        while (bytes >= 1024d && unit < Units.Length - 1)
        {
            bytes /= 1024d;
            unit++;
        }

        // Bayt ve kilobayt için ondalık göstermek gürültü yaratıyor.
        int places = unit <= 1 ? 0 : decimals;

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{Math.Round(bytes, places).ToString("N" + places, CultureInfo.CurrentCulture)} {Units[unit]}");
    }

    /// <summary>"2,3 GB / 465 GB" biçiminde ikili gösterim.</summary>
    public static string FormatRatio(long used, long total) => $"{Format(used)} / {Format(total)}";
}
