using SysScrub.Core.Disks;
using Xunit;

namespace SysScrub.Core.Tests.Disks;

/// <summary>
/// Öznitelik tablosu ve veri yolu numaralandırması.
///
/// Tablo derlemeye gömülü kaynak olarak geliyor; yüklenmezse kullanıcı ham
/// kimlikler görür. Veri yolu numaralandırması ise ntddstor.h ile birebir olmak
/// zorunda — bir kayma NVMe diski yanlış okuyucuya gönderiyor.
/// </summary>
public sealed class SmartAttributeTableTests
{
    private readonly SmartAttributeTable _table = new();

    [Fact]
    public void GomuluTabloYuklenir() => Assert.True(_table.Count > 30);

    [Fact]
    public void BilinenKimlikAdiylaBulunur()
    {
        SmartAttributeDefinition? definition = _table.Find(0x05);

        Assert.NotNull(definition);
        Assert.Equal("Yeniden atanan sektör sayısı", definition!.Name);
        Assert.True(definition.Critical);
    }

    [Fact]
    public void AciklamalarDolu()
    {
        foreach (byte id in (byte[])[0x05, 0xC5, 0xC6, 0xC7, 0xC2])
        {
            Assert.False(string.IsNullOrWhiteSpace(_table.Find(id)?.Description), $"0x{id:X2}");
        }
    }

    [Fact]
    public void TaninmayanKimlikBulunamaz() => Assert.Null(_table.Find(0x7B));

    /// <summary>
    /// Tanınmayan kimlik gizlenmiyor: üreticiye özel bir öznitelik olabilir ve
    /// ham değerini görmek teknisyen için hâlâ bilgi.
    /// </summary>
    [Fact]
    public void TaninmayanKimlikYineDeGosterilir()
    {
        SmartAttribute described = _table.Describe(new RawAtaAttribute(0x7B, 100, 100, 0, 42));

        Assert.Contains("0x7B", described.Name);
        Assert.Equal(42, described.Raw);
        Assert.False(described.IsCritical);
    }

    [Fact]
    public void TabloBilgisiOznitelikleAktarilir()
    {
        SmartAttribute described = _table.Describe(new RawAtaAttribute(0xC5, 100, 100, 0, 3));

        Assert.Equal("Bekleyen sektör sayısı", described.Name);
        Assert.True(described.IsCritical);
        Assert.Equal(DiskHealthStatus.Caution, described.Status);
    }

    [Fact]
    public void EsiginAltindakiOznitelikKotuSayilir()
    {
        SmartAttribute described = _table.Describe(new RawAtaAttribute(0x05, 30, 30, 36, 100));

        Assert.True(described.IsBelowThreshold);
        Assert.Equal(DiskHealthStatus.Bad, described.Status);
    }

    /// <summary>Eşiği sıfır olan öznitelik için üretici bir sınır tanımlamamış.</summary>
    [Fact]
    public void SifirEsikIhlalSayilmaz()
    {
        SmartAttribute described = _table.Describe(new RawAtaAttribute(0x09, 1, 1, 0, 2807));

        Assert.False(described.IsBelowThreshold);
    }

    [Fact]
    public void HamDegerOnIkiHanelikOnaltilikGosterilir() =>
        Assert.Equal("00000000002A", _table.Describe(new RawAtaAttribute(0x09, 100, 100, 0, 42)).RawHex);

    // ------------------------------------------------------------------ veri yolu

    [Theory]
    [InlineData(0x07, "USB")]
    [InlineData(0x0B, "SATA")]
    [InlineData(0x10, "Storage Spaces")]
    [InlineData(0x11, "NVMe")]
    public void VeriYoluTurleriNtddstorSirasiylaEslesir(uint busType, string expected) =>
        Assert.Equal(expected, DiskBusType.Describe(busType));

    /// <summary>
    /// Bu sabit yanlışsa NVMe disk "Storage Spaces" görünür ve sağlık verisi
    /// ATA okuyucusuna gider — hiçbir şey okunamaz.
    /// </summary>
    [Fact]
    public void NvmeSabitiOnYediDir() => Assert.Equal(0x11u, DiskBusType.Nvme);

    [Fact]
    public void BilinmeyenVeriYoluIsimlendirilir() => Assert.Equal("Bilinmiyor", DiskBusType.Describe(0xFF));
}
