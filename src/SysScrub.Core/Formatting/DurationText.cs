namespace SysScrub.Core.Formatting;

/// <summary>Süreleri okunabilir Türkçe metne çevirir. Arayüz ve komut satırı aynı biçimi kullanır.</summary>
public static class DurationText
{
    /// <summary>"3 gün 4 saat", "2 saat 19 dakika", "7 dakika", "42 saniye".</summary>
    public static string Humanize(TimeSpan duration)
    {
        duration = Clamp(duration);

        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays} gün {duration.Hours} saat";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} saat {duration.Minutes} dakika";
        }

        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes} dakika"
            : $"{(int)duration.TotalSeconds} saniye";
    }

    /// <summary>
    /// "… açık" biçimi. Ek son birime göre değiştiği için (saattir / dakikadır / gündür)
    /// Humanize'ın sonuna eklenemiyor, ayrı kurulması gerekiyor.
    /// </summary>
    public static string HumanizeUptime(TimeSpan uptime)
    {
        uptime = Clamp(uptime);

        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays} gün {uptime.Hours} saattir açık";
        }

        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours} saat {uptime.Minutes} dakikadır açık";
        }

        return uptime.TotalMinutes >= 1
            ? $"{(int)uptime.TotalMinutes} dakikadır açık"
            : $"{(int)uptime.TotalSeconds} saniyedir açık";
    }

    private static TimeSpan Clamp(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
