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

    [Theory]
    [InlineData(3, 4, 0, "3 gün 4 saattir açık")]
    [InlineData(0, 2, 19, "2 saat 19 dakikadır açık")]
    [InlineData(0, 0, 7, "7 dakikadır açık")]
    public void AcikKalmaEkiSonBirimeUyar(int days, int hours, int minutes, string expected) =>
        Assert.Equal(expected, DurationText.HumanizeUptime(new TimeSpan(days, hours, minutes, 0)));

    [Fact]
    public void BirDakikaAltiAcikKalmaSaniyeEkiAlir() =>
        Assert.Equal("30 saniyedir açık", DurationText.HumanizeUptime(TimeSpan.FromSeconds(30)));
}
