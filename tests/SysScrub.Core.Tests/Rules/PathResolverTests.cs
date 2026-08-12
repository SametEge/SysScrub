using SysScrub.Core.Rules;
using Xunit;

namespace SysScrub.Core.Tests.Rules;

public sealed class PathResolverTests : IDisposable
{
    private readonly PathResolver _resolver = new();
    private readonly string _sandbox;

    public PathResolverTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "SysScrub.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    // ------------------------------------------------------------------ Normalize

    [Fact]
    public void SondakiAyiriciKirpilir() =>
        Assert.Equal(@"C:\Windows", PathResolver.Normalize(@"C:\Windows\"));

    [Fact]
    public void SurucuKokundekiAyiriciKorunur() =>
        Assert.Equal(@"C:\", PathResolver.Normalize(@"C:\"));

    [Fact]
    public void UstKlasorIfadeleriCozulur() =>
        Assert.Equal(@"C:\Windows", PathResolver.Normalize(@"C:\Windows\System32\.."));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BosYolBosDoner(string path) =>
        Assert.Equal(string.Empty, PathResolver.Normalize(path));

    // ------------------------------------------------------------------ IsUnder

    [Fact]
    public void AltKlasorKokAltindaSayilir() =>
        Assert.True(PathResolver.IsUnder(@"C:\Veri\alt\dosya.txt", @"C:\Veri"));

    [Fact]
    public void KokunKendisiKokAltindaSayilir() =>
        Assert.True(PathResolver.IsUnder(@"C:\Veri", @"C:\Veri\"));

    [Fact]
    public void OnEkiAyniOlanKardesKlasorAltSayilmaz()
    {
        // Ayırıcı eklenmeseydi bu yanlışlıkla true dönerdi — gerçek bir güvenlik açığı.
        Assert.False(PathResolver.IsUnder(@"C:\Veriler\dosya.txt", @"C:\Veri"));
    }

    [Fact]
    public void BuyukKucukHarfFarkiOnemsiz() =>
        Assert.True(PathResolver.IsUnder(@"c:\veri\ALT\x.txt", @"C:\Veri"));

    [Fact]
    public void UstKlasorKokAltindaSayilmaz() =>
        Assert.False(PathResolver.IsUnder(@"C:\", @"C:\Veri"));

    // ------------------------------------------------------------------ Resolve

    [Fact]
    public void TemelTokenlarCozulur()
    {
        foreach (PathToken token in (PathToken[])
                 [PathToken.UserTemp, PathToken.LocalAppData, PathToken.ProgramData, PathToken.SystemRoot])
        {
            IReadOnlyList<string> paths = _resolver.GetBasePaths(token);

            Assert.NotEmpty(paths);
            Assert.True(Directory.Exists(paths[0]), $"{token} çözümlenen yol yok: {paths[0]}");
        }
    }

    [Fact]
    public void SabitSurucuKokleriBulunur()
    {
        IReadOnlyList<string> roots = _resolver.GetBasePaths(PathToken.AllFixedDrives);

        Assert.NotEmpty(roots);
        Assert.All(roots, root => Assert.True(Directory.Exists(root)));
    }

    [Fact]
    public void VarOlmayanGoreliYolBosDoner() =>
        Assert.Empty(_resolver.Resolve(PathToken.UserTemp, "kesinlikle/olmayan/klasor-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void JokerSegmentTumProfilleriAcar()
    {
        // Tarayıcı profillerinin keşfedildiği mekanizmanın aynısı.
        string root = Path.Combine(_sandbox, "Uygulama", "User Data");

        foreach (string profile in (string[])["Default", "Profile 1", "Profile 2"])
        {
            Directory.CreateDirectory(Path.Combine(root, profile, "Cache"));
        }

        Directory.CreateDirectory(Path.Combine(root, "Crashpad")); // Cache içermiyor, eşleşmemeli

        IReadOnlyList<string> resolved = ResolveUnderSandbox("Uygulama/User Data/*/Cache");

        Assert.Equal(3, resolved.Count);
        Assert.All(resolved, path => Assert.EndsWith("Cache", path));
    }

    [Fact]
    public void JokerSegmentAgacaInmez()
    {
        // "*" yalnızca bir seviye eşleşir; alt ağaca inip yanlışlıkla geniş alan açmaz.
        Directory.CreateDirectory(Path.Combine(_sandbox, "a", "b", "hedef"));
        Directory.CreateDirectory(Path.Combine(_sandbox, "a", "hedef"));

        IReadOnlyList<string> resolved = ResolveUnderSandbox("a/*/hedef");

        Assert.Single(resolved);
        Assert.Equal(
            PathResolver.Normalize(Path.Combine(_sandbox, "a", "b", "hedef")),
            PathResolver.Normalize(resolved[0]));
    }

    /// <summary>Sandbox geçici klasörün altında olduğu için UserTemp token'ıyla çözümlenebiliyor.</summary>
    private IReadOnlyList<string> ResolveUnderSandbox(string relativePath)
    {
        string sandboxName = Path.GetFileName(_sandbox);

        return _resolver.Resolve(PathToken.UserTemp, $"SysScrub.Tests/{sandboxName}/{relativePath}");
    }
}
