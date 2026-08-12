using SysScrub.Core.Rules;
using Xunit;

namespace SysScrub.Core.Tests.Rules;

public sealed class GlobPatternTests
{
    [Theory]
    [InlineData("**/*", "a.txt")]
    [InlineData("**/*", "alt/klasor/a.txt")]
    [InlineData("*.log", "hata.log")]
    [InlineData("**/*.log", "hata.log")]
    [InlineData("**/*.log", "logs/2026/hata.log")]
    [InlineData("thumbcache_*.db", "thumbcache_256.db")]
    [InlineData("Cookies", "Cookies")]
    [InlineData("cache?/veri", "cache1/veri")]
    public void EslesmesiGerekenler(string pattern, string path) =>
        Assert.True(GlobPattern.Parse(pattern).IsMatch(path), $"'{pattern}' deseni '{path}' yolunu eşlemeliydi");

    [Theory]
    [InlineData("*.log", "logs/hata.log")]
    [InlineData("*.log", "hata.txt")]
    [InlineData("thumbcache_*.db", "iconcache_32.db")]
    [InlineData("Cookies", "Cookies-journal")]
    [InlineData("cache?/veri", "cache12/veri")]
    public void EslesmemesiGerekenler(string pattern, string path) =>
        Assert.False(GlobPattern.Parse(pattern).IsMatch(path), $"'{pattern}' deseni '{path}' yolunu eşlememeliydi");

    [Fact]
    public void TekYildizAyiriciyiGecmez()
    {
        // Bu davranış önemli: "*" ağaca inseydi dar kapsamlı kurallar sessizce genişlerdi.
        Assert.False(GlobPattern.Parse("*").IsMatch("alt/dosya.txt"));
        Assert.True(GlobPattern.Parse("*").IsMatch("dosya.txt"));
    }

    [Fact]
    public void YolAyiriciTuruOnemsiz()
    {
        GlobPattern pattern = GlobPattern.Parse("logs/**/*.txt");

        Assert.True(pattern.IsMatch(@"logs\2026\a.txt"));
        Assert.True(pattern.IsMatch("logs/2026/a.txt"));
    }

    [Fact]
    public void BuyukKucukHarfFarkiOnemsiz() =>
        Assert.True(GlobPattern.Parse("**/*.LOG").IsMatch("klasor/hata.log"));

    [Fact]
    public void NoktaGercekNoktaOlarakEslenir()
    {
        // Regex'e çevrilirken kaçırılmazsa "." her karakteri eşler ve kural taşar.
        Assert.False(GlobPattern.Parse("a.txt").IsMatch("axtxt"));
    }

    [Fact]
    public void AyniDesenOnbellektenDoner() =>
        Assert.Same(GlobPattern.Parse("**/*.tmp"), GlobPattern.Parse("**/*.tmp"));
}
