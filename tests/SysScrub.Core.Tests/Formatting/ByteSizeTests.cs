using System.Globalization;
using SysScrub.Core.Formatting;
using Xunit;

namespace SysScrub.Core.Tests.Formatting;

public sealed class ByteSizeTests
{
    public ByteSizeTests()
    {
        // Ondalık ayracı kültüre göre değiştiği için testleri sabit kültüre bağlıyoruz.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(-1L, "0 B")]
    [InlineData(512L, "512 B")]
    public void SifirVeAltiSifirOlarakGosterilir(long bytes, string expected) =>
        Assert.Equal(expected, ByteSize.Format(bytes));

    [Fact]
    public void KilobaytaKadarOndalikGosterilmez()
    {
        Assert.Equal("2 KB", ByteSize.Format(2048L));
        Assert.Equal("1,023 KB", ByteSize.Format(1024L * 1023));
    }

    [Fact]
    public void MegabayttanItibarenOndalikGosterilir()
    {
        Assert.Equal("1.5 MB", ByteSize.Format(1024L * 1024 * 3 / 2));
        Assert.Equal("2.0 GB", ByteSize.Format(1024L * 1024 * 1024 * 2));
    }

    [Fact]
    public void WindowsGibi1024TabaniKullanilir()
    {
        // 500 GB'lık bir disk Explorer'da 465 GB görünür; aynı sayıyı üretmeliyiz.
        const long fiveHundredGigabytesOnTheBox = 500_000_000_000L;

        Assert.Equal("465.7 GB", ByteSize.Format(fiveHundredGigabytesOnTheBox));
    }

    [Fact]
    public void EnBuyukBirimAsilmaz()
    {
        string formatted = ByteSize.Format(long.MaxValue);

        Assert.EndsWith("PB", formatted);
    }

    [Fact]
    public void OranIkiliGosterimUretir() =>
        Assert.Equal("1.0 GB / 2.0 GB", ByteSize.FormatRatio(1024L * 1024 * 1024, 1024L * 1024 * 1024 * 2));
}
