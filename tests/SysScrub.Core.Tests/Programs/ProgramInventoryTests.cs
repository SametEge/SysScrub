using SysScrub.Core.Programs;
using Xunit;

namespace SysScrub.Core.Tests.Programs;

/// <summary>Envanterin ayrıştırma ve özetleme davranışı.</summary>
public sealed class ProgramInventoryTests
{
    // ------------------------------------------------------------------ kurulum tarihi

    [Fact]
    public void KurulumTarihiYyyyAaGgOkunur() =>
        Assert.Equal(new DateTime(2026, 8, 3), ProgramInventory.ParseInstallDate("20260803"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bozuk")]
    public void OkunamayanTarihGosterilmez(string? raw) =>
        Assert.Null(ProgramInventory.ParseInstallDate(raw));

    // ------------------------------------------------------------------ kurulum yolu

    [Fact]
    public void YolSonundakiTersBoluAtilir() =>
        Assert.Equal(@"C:\Program Files\Git", ProgramInventory.NormalizeLocation(@"C:\Program Files\Git\"));

    [Fact]
    public void YoldakiTirnaklarAtilir() =>
        Assert.Equal(@"C:\Program Files\Git", ProgramInventory.NormalizeLocation(@"""C:\Program Files\Git"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BosYolNullOlur(string? raw) => Assert.Null(ProgramInventory.NormalizeLocation(raw));

    // ------------------------------------------------------------------ Store paket kimliği

    [Fact]
    public void PaketKimligindenAdCikarilir() =>
        Assert.Equal("WhatsAppDesktop",
            ProgramInventory.NameFromPackageId("5319275A.WhatsAppDesktop_2.2630.102.0_x64__cv1g1gvanyjgm"));

    [Fact]
    public void NoktasizPaketKimligiOlduguGibiKalir() =>
        Assert.Equal("Claude", ProgramInventory.NameFromPackageId("Claude_1.28929.0.0_x64__pzs8sxrjxfjjc"));

    [Fact]
    public void PaketKimligindenYayinciCikarilir() =>
        Assert.Equal("Microsoft",
            ProgramInventory.PublisherFromPackageId("Microsoft.BingNews_4.56.21872.0_x64__8wekyb3d8bbwe"));

    [Fact]
    public void PaketKimligindenSurumCikarilir() =>
        Assert.Equal("4.56.21872.0",
            ProgramInventory.VersionFromPackageId("Microsoft.BingNews_4.56.21872.0_x64__8wekyb3d8bbwe"));

    // ------------------------------------------------------------------ boyut

    private static InstalledProgram Program(
        string name = "Ornek",
        long registryBytes = 0,
        long? measuredBytes = null,
        bool component = false,
        ProgramSource source = ProgramSource.Registry,
        string? uninstall = @"C:\Program Files\Ornek\unins.exe") => new()
    {
        Id = name,
        Name = name,
        Source = source,
        RegistrySizeBytes = registryBytes,
        MeasuredSizeBytes = measuredBytes,
        IsSystemComponent = component,
        UninstallCommand = uninstall
    };

    /// <summary>
    /// Kayıttaki tahmin çoğu zaman kurulum anından kalma. Ölçüm varsa o kazanır.
    /// </summary>
    [Fact]
    public void OlculenBoyutKaydinTahminiYerineGecer() =>
        Assert.Equal(500, Program(registryBytes: 100, measuredBytes: 500).SizeBytes);

    [Fact]
    public void OlcumYokkenKaydinTahminiKullanilir() =>
        Assert.Equal(100, Program(registryBytes: 100).SizeBytes);

    /// <summary>Boyut bilinmiyorsa "0 B" değil "—" gösterilecek; sıfır yazmak yanıltıcı.</summary>
    [Fact]
    public void BilinmeyenBoyutSifirSayilmaz() => Assert.False(Program().HasSize);

    // ------------------------------------------------------------------ kaldırılabilirlik

    [Fact]
    public void KomutuOlmayanProgramKaldirilamaz() =>
        Assert.False(Program(uninstall: null).CanUninstall);

    [Fact]
    public void StorePaketiPaketAdiylaKaldirilir()
    {
        InstalledProgram store = Program(source: ProgramSource.Store, uninstall: null) with
        {
            PackageFullName = "Claude_1.0.0.0_x64__abc"
        };

        Assert.True(store.CanUninstall);
        Assert.True(store.SupportsQuietUninstall);
    }

    [Fact]
    public void PaketAdiOlmayanStoreKaydiKaldirilamaz() =>
        Assert.False(Program(source: ProgramSource.Store, uninstall: null).CanUninstall);

    // ------------------------------------------------------------------ özet

    [Fact]
    public void GizliBilesenlerGorunurSayiyaGirmez()
    {
        var report = new ProgramInventoryReport
        {
            Programs = [Program("A"), Program("B"), Program("C", component: true)],
            Duration = TimeSpan.Zero
        };

        Assert.Equal(2, report.VisibleCount);
        Assert.Equal(1, report.ComponentCount);
    }

    [Fact]
    public void ToplamBoyutGizliBilesenleriSaymaz()
    {
        var report = new ProgramInventoryReport
        {
            Programs =
            [
                Program("A", measuredBytes: 1000),
                Program("B", measuredBytes: 2000),
                Program("C", measuredBytes: 9000, component: true)
            ],
            Duration = TimeSpan.Zero
        };

        Assert.Equal(3000, report.KnownSizeBytes);
    }

    [Fact]
    public void StorePaketleriAyriSayilir()
    {
        var report = new ProgramInventoryReport
        {
            Programs = [Program("A"), Program("B", source: ProgramSource.Store)],
            Duration = TimeSpan.Zero
        };

        Assert.Equal(1, report.StoreCount);
    }
}
