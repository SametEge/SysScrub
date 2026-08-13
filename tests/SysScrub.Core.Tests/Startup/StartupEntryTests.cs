using SysScrub.Core.Startup;
using Xunit;

namespace SysScrub.Core.Tests.Startup;

/// <summary>Etiket ve özet hesapları. Kullanıcının ekranda okuduğu her sayı buradan çıkıyor.</summary>
public sealed class StartupEntryTests
{
    private static StartupEntry Entry(
        string name = "Ornek",
        bool enabled = true,
        int? delayMs = null,
        bool targetMissing = false) => new()
    {
        Id = name,
        Name = name,
        Command = $@"C:\Program Files\{name}\{name}.exe",
        Source = StartupSource.RegistryRun,
        IsEnabled = enabled,
        BootDelayMs = delayMs,
        TargetMissing = targetMissing
    };

    // ------------------------------------------------------------------ etki etiketi

    [Theory]
    [InlineData(null, "ölçülmedi")]
    [InlineData(120, "düşük")]
    [InlineData(299, "düşük")]
    [InlineData(300, "orta")]
    [InlineData(999, "orta")]
    [InlineData(1000, "yüksek")]
    [InlineData(4200, "yüksek")]
    public void EtkiEtiketiOlcumeGoreVerilir(int? delayMs, string expected) =>
        Assert.Equal(expected, Entry(delayMs: delayMs).ImpactLabel);

    /// <summary>
    /// Ölçüm yoksa "düşük etki" demek uydurmak olur; rakiplerin yaptığı tam olarak bu.
    /// </summary>
    [Fact]
    public void OlcumYoksaEtkiIddiaEdilmez() =>
        Assert.Equal("ölçülmedi", Entry(delayMs: null).ImpactLabel);

    // ------------------------------------------------------------------ kaynak etiketi

    [Fact]
    public void MakineGenelindekiKayitAyriEtiketAlir()
    {
        StartupEntry entry = Entry() with { IsMachineWide = true };

        Assert.Equal("Kayıt defteri (tüm kullanıcılar)", entry.SourceLabel);
    }

    [Fact]
    public void KullaniciKaydiSadeEtiketAlir() =>
        Assert.Equal("Kayıt defteri", Entry().SourceLabel);

    [Theory]
    [InlineData(StartupSource.RegistryRunOnce, "Kayıt defteri (bir kerelik)")]
    [InlineData(StartupSource.ScheduledTask, "Zamanlanmış görev")]
    [InlineData(StartupSource.Service, "Servis")]
    public void HerKaynakKendiEtiketiniAlir(StartupSource source, string expected) =>
        Assert.Equal(expected, (Entry() with { Source = source }).SourceLabel);

    // ------------------------------------------------------------------ özet

    [Fact]
    public void ToplamGecikmeYalnizcaAcikOgelerdenHesaplanir()
    {
        var report = new StartupInventoryReport
        {
            Entries =
            [
                Entry("A", delayMs: 500),
                Entry("B", delayMs: 1500),
                Entry("C", enabled: false, delayMs: 9000)
            ],
            Duration = TimeSpan.Zero
        };

        // Kapalı öğe açılışı geciktirmiyor; toplama katılmamalı.
        Assert.Equal(2000, report.TotalDelayMs);
    }

    [Fact]
    public void AcikVeKapaliSayilariAyriTutulur()
    {
        var report = new StartupInventoryReport
        {
            Entries = [Entry("A"), Entry("B"), Entry("C", enabled: false)],
            Duration = TimeSpan.Zero
        };

        Assert.Equal(2, report.EnabledCount);
        Assert.Equal(1, report.DisabledCount);
    }

    [Fact]
    public void HedefiKayipOgelerAyriListelenir()
    {
        var report = new StartupInventoryReport
        {
            Entries = [Entry("A"), Entry("B", targetMissing: true)],
            Duration = TimeSpan.Zero
        };

        StartupEntry broken = Assert.Single(report.BrokenEntries);
        Assert.Equal("B", broken.Name);
    }

    [Fact]
    public void OlcumYokkenToplamGecikmeSifirdir()
    {
        var report = new StartupInventoryReport
        {
            Entries = [Entry("A"), Entry("B")],
            Duration = TimeSpan.Zero
        };

        Assert.Equal(0, report.TotalDelayMs);
        Assert.False(report.BootMeasurementsAvailable);
    }

    [Fact]
    public void ServislerVarsayilanOlarakDegistirilemez()
    {
        // Envanterde servisler bilerek salt okunur işaretleniyor; model bunu taşımalı.
        StartupEntry service = Entry() with { Source = StartupSource.Service, Control = StartupControl.ReadOnly };

        Assert.Equal(StartupControl.ReadOnly, service.Control);
        Assert.Equal(StartupControl.Toggleable, Entry().Control);
    }
}
