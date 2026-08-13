using SysScrub.Core.Settings;
using Xunit;

namespace SysScrub.Core.Tests.Settings;

/// <summary>
/// Dil ve karşılama turu ayarları.
///
/// Varsayılan "auto": yeni kurulumda kullanıcı hiçbir şey seçmeden kendi dilini
/// görüyor. Turun varsayılanı gösterilmesi, çünkü ilk açılışta kimse arayüzü
/// bilmiyor.
/// </summary>
public sealed class LanguageSettingsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "SysScrub.Tests", Guid.NewGuid().ToString("N"));

    public LanguageSettingsTests() => Directory.CreateDirectory(_directory);

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
    public void VarsayilanDilOtomatiktir() =>
        Assert.Equal(AppSettings.AutomaticLanguage, AppSettings.Default.Language);

    [Fact]
    public void VarsayilanOlarakTurGosterilir() => Assert.False(AppSettings.Default.TourCompleted);

    [Fact]
    public void DilSecimiDiskeYazilir()
    {
        Store().Update(s => s with { Language = "ja" });

        Assert.Equal("ja", Store().Current.Language);
    }

    [Fact]
    public void TurTamamlandiIsaretiKalicidir()
    {
        Store().Update(s => s with { TourCompleted = true });

        Assert.True(Store().Current.TourCompleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BosDilOtomatigeDoner(string? language) =>
        Assert.Equal(
            AppSettings.AutomaticLanguage,
            (AppSettings.Default with { Language = language! }).Normalized().Language);

    /// <summary>Elle düzenlenmiş dosyada boşluk kalmış olabilir.</summary>
    [Fact]
    public void DilKodununBosluklariTemizlenir() =>
        Assert.Equal("de", (AppSettings.Default with { Language = "  de  " }).Normalized().Language);

    [Fact]
    public void EskiAyarDosyasiDilAlaniOlmadanOkunur()
    {
        // 0.10.0 öncesinden kalan dosyada dil alanı yok; varsayılanla doldurulmalı.
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{ "theme": "Dark", "quarantineRetentionDays": 14 }""");

        AppSettings settings = Store().Current;

        Assert.Equal(AppSettings.AutomaticLanguage, settings.Language);
        Assert.False(settings.TourCompleted);
        Assert.Equal(14, settings.QuarantineRetentionDays);
    }
}
