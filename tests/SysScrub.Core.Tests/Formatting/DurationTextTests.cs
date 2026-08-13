using System.Globalization;
using SysScrub.Core.Formatting;
using Xunit;

namespace SysScrub.Core.Tests.Formatting;

public sealed class DurationTextTests
{
    // ------------------------------------------------------------------ kısa gecikmeler

    [Fact]
    public void SaniyeAltiMilisaniyeOlarakYazilir() =>
        Assert.Equal("420 ms", DurationText.FromMilliseconds(420));

    [Fact]
    public void SaniyeGecinceOndalikliSaniyeYazilir() =>
        Assert.Equal(
            $"{1.3.ToString("N1", CultureInfo.CurrentCulture)} sn",
            DurationText.FromMilliseconds(1300));

    /// <summary>On saniyeden sonra ondalık bilgi taşımıyor, gürültü yapıyor.</summary>
    [Fact]
    public void OnSaniyeUstundeOndalikAtilir() =>
        Assert.Equal("12 sn", DurationText.FromMilliseconds(12400));

    [Fact]
    public void OlcumYoksaBosMetinDoner()
    {
        Assert.Equal(string.Empty, DurationText.FromMilliseconds(0));
        Assert.Equal(string.Empty, DurationText.FromMilliseconds(-5));
    }

    [Fact]
    public void GunVarsaGunVeSaatGosterilir() =>
        Assert.Equal("3 gün 4 saat", DurationText.Humanize(new TimeSpan(3, 4, 30, 0)));

    [Fact]
    public void SaatVarsaSaatVeDakikaGosterilir() =>
        Assert.Equal("2 saat 19 dakika", DurationText.Humanize(new TimeSpan(2, 19, 45)));

    [Fact]
    public void BirSaatinAltindaDakikaGosterilir() =>
        Assert.Equal("7 dakika", DurationText.Humanize(TimeSpan.FromMinutes(7.9)));

    [Fact]
    public void BirDakikaninAltindaSaniyeGosterilir() =>
        Assert.Equal("42 saniye", DurationText.Humanize(TimeSpan.FromSeconds(42)));

    [Fact]
    public void NegatifSureSifirlanir() =>
        Assert.Equal("0 saniye", DurationText.Humanize(TimeSpan.FromSeconds(-10)));

    // ------------------------------------------------------------------ birim sözcükleri

    /// <summary>
    /// Sözcükler dışarıdan geliyor: arayüz dili değişince aynı hesap başka dilde
    /// yazılmalı, sayıların yeniden hesaplanması gerekmemeli.
    /// </summary>
    [Fact]
    public void BirimSozcukleriDegistirilebilir()
    {
        DurationWords original = DurationText.Words;

        try
        {
            DurationText.Words = new DurationWords("days", "hours", "minutes", "seconds", "s");

            Assert.Equal("3 days 4 hours", DurationText.Humanize(new TimeSpan(3, 4, 30, 0)));
            Assert.Equal("2 hours 19 minutes", DurationText.Humanize(new TimeSpan(2, 19, 45)));
            Assert.Equal("7 minutes", DurationText.Humanize(TimeSpan.FromMinutes(7.9)));
            Assert.Equal("42 seconds", DurationText.Humanize(TimeSpan.FromSeconds(42)));
            Assert.Equal("12 s", DurationText.FromMilliseconds(12400));

            // Milisaniye her dilde "ms"; çevrilecek bir şey yok.
            Assert.Equal("420 ms", DurationText.FromMilliseconds(420));
        }
        finally
        {
            DurationText.Words = original;
        }
    }

    [Fact]
    public void VarsayilanSozcuklerTurkce() =>
        Assert.Equal(DurationWords.Turkish, DurationText.Words);
}
