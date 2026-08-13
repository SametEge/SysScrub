using SysScrub.Core.Settings;
using Xunit;

namespace SysScrub.Core.Tests.Settings;

/// <summary>
/// Ayar kalıcılığı.
///
/// En kritik davranış: bozuk ya da elle düzenlenmiş bir ayar dosyası uygulamayı
/// düşürmemeli. Ayar dosyası yüzünden açılmayan bir bakım aracı işe yaramaz.
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "SysScrub.Tests", Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private SettingsStore Store() => new(_directory);

    [Fact]
    public void DosyaYokkenVarsayilanlarKullanilir()
    {
        AppSettings settings = Store().Current;

        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.Equal(AppSettings.DefaultRetentionDays, settings.QuarantineRetentionDays);
        Assert.True(settings.CreateRestorePoint);
        Assert.False(settings.ScheduledCleanup);
    }

    [Fact]
    public void DegisiklikDiskeYazilirVeGeriOkunur()
    {
        Store().Update(s => s with { Theme = ThemePreference.Dark, QuarantineRetentionDays = 30 });

        AppSettings reloaded = Store().Current;

        Assert.Equal(ThemePreference.Dark, reloaded.Theme);
        Assert.Equal(30, reloaded.QuarantineRetentionDays);
    }

    [Fact]
    public void AyniDegerYenidenYazilmaz()
    {
        SettingsStore store = Store();
        var raised = 0;

        store.Changed += (_, _) => raised++;

        store.Update(s => s with { Theme = ThemePreference.Dark });
        store.Update(s => s with { Theme = ThemePreference.Dark });

        Assert.Equal(1, raised);
    }

    [Fact]
    public void DegisiklikOlayiYeniAyarlariTasir()
    {
        SettingsStore store = Store();
        AppSettings? received = null;

        store.Changed += (_, settings) => received = settings;

        store.Update(s => s with { QuarantineRetentionDays = 21 });

        Assert.Equal(21, received?.QuarantineRetentionDays);
    }

    // ------------------------------------------------------------------ dayanıklılık

    /// <summary>
    /// Bozuk dosya yüzünden uygulamanın açılmaması kabul edilemez;
    /// varsayılanlara dönülüyor.
    /// </summary>
    [Fact]
    public void BozukDosyaVarsayilanlaraDoner()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ bu json değil ]]]");

        Assert.Equal(AppSettings.Default, Store().Current);
    }

    [Fact]
    public void BosDosyaVarsayilanlaraDoner()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), string.Empty);

        Assert.Equal(AppSettings.Default, Store().Current);
    }

    /// <summary>Eksik alanlar varsayılanla doldurulur; eski sürümden gelen dosya çalışmaya devam eder.</summary>
    [Fact]
    public void EksikAlanlarVarsayilanAlir()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{ "theme": "Dark" }""");

        AppSettings settings = Store().Current;

        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.Equal(AppSettings.DefaultRetentionDays, settings.QuarantineRetentionDays);
    }

    [Fact]
    public void DosyaOkunabilirJsonOlarakYazilir()
    {
        Store().Update(s => s with { Theme = ThemePreference.Light });

        string json = File.ReadAllText(Path.Combine(_directory, "settings.json"));

        // Teknisyen elle düzenleyebilsin: sayı değil, ad olarak yazılıyor.
        Assert.Contains("\"Light\"", json);
        Assert.Contains("quarantineRetentionDays", json);
    }

    // ------------------------------------------------------------------ sınırlar

    [Theory]
    [InlineData(0, AppSettings.MinimumRetentionDays)]
    [InlineData(-5, AppSettings.MinimumRetentionDays)]
    [InlineData(500, AppSettings.MaximumRetentionDays)]
    [InlineData(30, 30)]
    public void SaklamaSuresiSinirlaraCekilir(int input, int expected) =>
        Assert.Equal(expected, (AppSettings.Default with { QuarantineRetentionDays = input })
            .Normalized().QuarantineRetentionDays);

    /// <summary>Sıfır gün saklama karantinayı anlamsız kılardı.</summary>
    [Fact]
    public void ElleSifirYazilmisDosyaDuzeltilir()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{ "quarantineRetentionDays": 0 }""");

        Assert.Equal(AppSettings.MinimumRetentionDays, Store().Current.QuarantineRetentionDays);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(24, 23)]
    [InlineData(3, 3)]
    public void CalismaSaatiGunIcindeKalir(int input, int expected) =>
        Assert.Equal(expected, (AppSettings.Default with { ScheduledHour = input }).Normalized().ScheduledHour);

    [Fact]
    public void TanimsizTemaSistemeDoner() =>
        Assert.Equal(
            ThemePreference.System,
            (AppSettings.Default with { Theme = (ThemePreference)99 }).Normalized().Theme);

    [Fact]
    public void SaklamaSuresiZamanAraligiOlarakVerilir() =>
        Assert.Equal(
            TimeSpan.FromDays(14),
            (AppSettings.Default with { QuarantineRetentionDays = 14 }).QuarantineRetention);
}
