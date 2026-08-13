using System.Globalization;

namespace SysScrub.Core.Formatting;

/// <summary>
/// Süreleri okunabilir metne çevirir. Arayüz ve komut satırı aynı biçimi kullanır.
///
/// Sayılar burada, sözcükler <see cref="Words"/>'te: arayüz açılışta kendi
/// kataloğunu bağlıyor, komut satırı varsayılan Türkçe'de kalıyor.
/// </summary>
public static class DurationText
{
    /// <summary>Birim sözcükleri. Arayüz dil değiştikçe bunu yeniliyor.</summary>
    public static DurationWords Words { get; set; } = DurationWords.Turkish;

    /// <summary>
    /// Kısa gecikme gösterimi: "420 ms", "1,3 sn", "12 sn".
    /// Açılış gecikmeleri milisaniye geliyor ve saniyeye yuvarlamak farkı siliyor.
    /// </summary>
    public static string FromMilliseconds(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return string.Empty;
        }

        if (milliseconds < 1000)
        {
            return $"{milliseconds} ms";
        }

        double seconds = milliseconds / 1000d;

        // 10 saniyeden sonra ondalık bilgi taşımıyor.
        return seconds >= 10
            ? $"{Math.Round(seconds).ToString("N0", CultureInfo.CurrentCulture)} {Words.ShortSecond}"
            : $"{seconds.ToString("N1", CultureInfo.CurrentCulture)} {Words.ShortSecond}";
    }

    /// <summary>"3 gün 4 saat", "2 saat 19 dakika", "7 dakika", "42 saniye".</summary>
    public static string Humanize(TimeSpan duration)
    {
        duration = Clamp(duration);

        DurationWords words = Words;

        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays} {words.Day} {duration.Hours} {words.Hour}";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} {words.Hour} {duration.Minutes} {words.Minute}";
        }

        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes} {words.Minute}"
            : $"{(int)duration.TotalSeconds} {words.Second}";
    }

    private static TimeSpan Clamp(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
