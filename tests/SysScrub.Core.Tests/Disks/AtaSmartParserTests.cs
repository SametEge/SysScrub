using SysScrub.Core.Disks;
using Xunit;

namespace SysScrub.Core.Tests.Disks;

/// <summary>
/// ATA S.M.A.R.T. öznitelik tablosunun ayrıştırılması.
///
/// Tablo 12 baytlık girişlerden oluşuyor ve 2. bayttan başlıyor. Eşikler ayrı
/// bir komutla gelen ikinci tampodan eşleştiriliyor — kimliğe göre, konuma göre
/// değil: iki tablonun sırası aynı olmak zorunda değil.
/// </summary>
public sealed class AtaSmartParserTests
{
    /// <summary>Verilen girişlerle 512 baytlık öznitelik tamponu kurar.</summary>
    private static byte[] BuildValues(params (byte Id, byte Current, byte Worst, long Raw)[] entries)
    {
        var buffer = new byte[512];
        buffer[0] = 0x10;

        for (int i = 0; i < entries.Length; i++)
        {
            int offset = 2 + (i * 12);

            buffer[offset] = entries[i].Id;
            buffer[offset + 3] = entries[i].Current;
            buffer[offset + 4] = entries[i].Worst;

            long raw = entries[i].Raw;

            for (int b = 0; b < 6; b++)
            {
                buffer[offset + 5 + b] = (byte)(raw >> (b * 8));
            }
        }

        return buffer;
    }

    private static byte[] BuildThresholds(params (byte Id, byte Threshold)[] entries)
    {
        var buffer = new byte[512];

        for (int i = 0; i < entries.Length; i++)
        {
            int offset = 2 + (i * 12);

            buffer[offset] = entries[i].Id;
            buffer[offset + 1] = entries[i].Threshold;
        }

        return buffer;
    }

    [Fact]
    public void OzniteliklerSirayaGoreOkunur()
    {
        IReadOnlyList<RawAtaAttribute> attributes = AtaHealthReader.Parse(
            BuildValues((0x05, 100, 100, 0), (0x09, 95, 95, 2807)),
            BuildThresholds());

        Assert.Equal(2, attributes.Count);
        Assert.Equal(0x05, attributes[0].Id);
        Assert.Equal(0x09, attributes[1].Id);
        Assert.Equal(2807, attributes[1].Raw);
    }

    /// <summary>Kimliği sıfır olan giriş kullanılmıyor demek; listeye alınmamalı.</summary>
    [Fact]
    public void KullanilmayanGirisAtlanir()
    {
        IReadOnlyList<RawAtaAttribute> attributes = AtaHealthReader.Parse(
            BuildValues((0x00, 0, 0, 0), (0x05, 100, 100, 0)),
            BuildThresholds());

        Assert.Equal(0x05, Assert.Single(attributes).Id);
    }

    /// <summary>Ham değer 48 bit, küçük endian. Sıralama hatası sayıyı milyonlarca kat şişirir.</summary>
    [Fact]
    public void HamDegerKirkSekizBitKucukEndianOkunur()
    {
        IReadOnlyList<RawAtaAttribute> attributes = AtaHealthReader.Parse(
            BuildValues((0xF1, 100, 100, 0x0000_1234_5678)),
            BuildThresholds());

        Assert.Equal(0x0000_1234_5678, Assert.Single(attributes).Raw);
    }

    [Fact]
    public void EnBuyukKirkSekizBitDegerTasmaz()
    {
        IReadOnlyList<RawAtaAttribute> attributes = AtaHealthReader.Parse(
            BuildValues((0xF1, 100, 100, 0xFFFF_FFFF_FFFF)),
            BuildThresholds());

        Assert.Equal(0xFFFF_FFFF_FFFF, Assert.Single(attributes).Raw);
    }

    /// <summary>
    /// Eşik tablosunun sırası öznitelik tablosuyla aynı olmak zorunda değil;
    /// eşleştirme kimliğe göre yapılmalı.
    /// </summary>
    [Fact]
    public void EsiklerKimligeGoreEslesir()
    {
        IReadOnlyList<RawAtaAttribute> attributes = AtaHealthReader.Parse(
            BuildValues((0x05, 100, 100, 0), (0x09, 95, 95, 0)),
            BuildThresholds((0x09, 0), (0x05, 36)));

        Assert.Equal(36, attributes.Single(a => a.Id == 0x05).Threshold);
        Assert.Equal(0, attributes.Single(a => a.Id == 0x09).Threshold);
    }

    [Fact]
    public void EsikTablosuOkunamazsaOznitelikYineDoner()
    {
        IReadOnlyList<RawAtaAttribute> attributes = AtaHealthReader.Parse(
            BuildValues((0x05, 100, 100, 0)),
            ReadOnlySpan<byte>.Empty);

        Assert.Equal(0, Assert.Single(attributes).Threshold);
    }

    [Fact]
    public void OtuzOzniteliktenFazlasiOkunmaz()
    {
        var entries = new (byte, byte, byte, long)[40];

        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = ((byte)(i + 1), 100, 100, 0);
        }

        // 512 baytlık tampona 30 girişten fazlası zaten sığmıyor.
        Assert.Equal(30, AtaHealthReader.Parse(BuildValues(entries), BuildThresholds()).Count);
    }
}
