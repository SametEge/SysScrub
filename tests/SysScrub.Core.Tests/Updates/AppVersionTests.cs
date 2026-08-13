using SysScrub.Core.Updates;
using Xunit;

namespace SysScrub.Core.Tests.Updates;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData("V0.13.0-alpha", 0, 13, 0, "alpha")]
    [InlineData("1.2.3-rc.1+abc123", 1, 2, 3, "rc.1")]
    [InlineData("  2.0  ", 2, 0, 0, "")]
    [InlineData("3", 3, 0, 0, "")]
    public void Cozumleme_bicimleri_kabul_eder(string text, int major, int minor, int patch, string preRelease)
    {
        Assert.True(AppVersion.TryParse(text, out AppVersion version));

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(preRelease, version.PreRelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("sürüm")]
    [InlineData("1.2.3.4")]
    [InlineData("v-alpha")]
    [InlineData("1.x.3")]
    [InlineData("-1.2.3")]
    public void Gecersiz_metin_reddedilir(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Fact]
    public void Sayilar_soldan_saga_karsilastirilir()
    {
        Assert.True(AppVersion.Parse("1.0.0") > AppVersion.Parse("0.99.99"));
        Assert.True(AppVersion.Parse("0.14.0") > AppVersion.Parse("0.13.9"));
        Assert.True(AppVersion.Parse("0.13.2") > AppVersion.Parse("0.13.1"));
    }

    /// <summary>
    /// Bu kural olmasa alfa kullanan kişi aynı numaralı kararlı sürüme hiç geçemezdi.
    /// </summary>
    [Fact]
    public void Kararli_surum_ayni_numarali_on_yayindan_buyuktur()
    {
        Assert.True(AppVersion.Parse("0.13.0") > AppVersion.Parse("0.13.0-alpha"));
        Assert.True(AppVersion.Parse("1.0.0") > AppVersion.Parse("1.0.0-rc.9"));
    }

    [Fact]
    public void On_yayin_etiketleri_sirali_karsilastirilir()
    {
        Assert.True(AppVersion.Parse("1.0.0-beta") > AppVersion.Parse("1.0.0-alpha"));
        Assert.True(AppVersion.Parse("1.0.0-rc.2") > AppVersion.Parse("1.0.0-rc.1"));

        // Sayısal parça sayı olarak karşılaştırılmalı: metin sırasında 10 < 2 olurdu.
        Assert.True(AppVersion.Parse("1.0.0-rc.10") > AppVersion.Parse("1.0.0-rc.2"));

        // Daha az parçalı etiket küçüktür (SemVer kuralı).
        Assert.True(AppVersion.Parse("1.0.0-rc.1") > AppVersion.Parse("1.0.0-rc"));
    }

    [Fact]
    public void Ayni_surum_esittir()
    {
        Assert.Equal(AppVersion.Parse("v1.2.3-alpha"), AppVersion.Parse("1.2.3-alpha+build"));
        Assert.Equal(0, AppVersion.Parse("1.2.3").CompareTo(AppVersion.Parse("1.2.3")));
    }

    [Fact]
    public void Metin_hali_etiketle_ayni_bicimde_yazilir()
    {
        Assert.Equal("0.13.0-alpha", AppVersion.Parse("v0.13.0-alpha").ToString());
        Assert.Equal("v1.0.0", AppVersion.Parse("1.0.0").ToTag());
    }

    [Fact]
    public void Calisan_derlemenin_surumu_okunabiliyor()
    {
        AppVersion current = AppVersion.FromAssembly(typeof(AppVersion).Assembly);

        Assert.False(current.IsEmpty);
    }
}
